using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.ServiceModel;
using System.Threading.Tasks;

namespace RevitServerNet.Enterprise
{
	internal sealed class RsModelExporterOptions
	{
		public string ServerHost { get; set; }
		public string ModelPipePath { get; set; }
		public string DestinationFile { get; set; }
		public string RevitVersion { get; set; }
		public string AssembliesPath { get; set; }
		public bool Overwrite { get; set; }
	}

	internal sealed class RsModelExporter
	{
		public async Task<string> ExportAsync(RsModelExporterOptions options, IProgress<long> bytesProgress = null)
		{
			ValidateOptions(options);

			var assemblies = RsAssemblyLoader.Load(options.RevitVersion, options.AssembliesPath);
			bool UseEnvFlag(string name)
			{
				try { return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)); } catch { return false; }
			}

			(object proxyProviderInstance, Type iModelServiceType) stageProvider()
				=> CreateProxyProviderAndModelServiceType(assemblies, options.ServerHost, options.RevitVersion, out var _, out var _);
			(object proxyProvider, Type iModel, MethodInfo getBufferedProxy, MethodInfo getStreamedProxy) initProvider()
			{
				var (pp, iModel) = CreateProxyProviderAndModelServiceType(assemblies, options.ServerHost, options.RevitVersion, out var gb, out var gs);
				return (pp, iModel, gb, gs);
			}

			object serviceSessionToken;
			object modelIdentity;
			object serviceModelSessionToken;
			object creationDate;
			(string stage, Exception error) Fail(string stage, Exception ex) => (stage, ex);

			(object proxyProvider, Type iModelServiceType, MethodInfo getBufferedProxy, MethodInfo getStreamedProxy) provider;
			try { provider = initProvider(); }
			catch (Exception ex) { throw new InvalidOperationException($"[Provider] Failed to initialize ProxyProvider. Base='{assemblies.BaseDirectory}'. {ex.GetBaseException().Message}", ex); }

			try { serviceSessionToken = CreateServiceSessionToken(assemblies); }
			catch (Exception ex) { throw new InvalidOperationException($"[ServiceSessionToken] Failed to create. Base='{assemblies.BaseDirectory}'. {ex.GetBaseException().Message}", ex); }

            try { modelIdentity = IdentifyModel(assemblies, provider.proxyProvider, provider.iModelServiceType, provider.getStreamedProxy, serviceSessionToken, options.ServerHost, options.ModelPipePath); }
			catch (Exception ex) { throw new InvalidOperationException($"[IdentifyModel] Failed. Model='{options.ModelPipePath}'. Host='{options.ServerHost}'. Base='{assemblies.BaseDirectory}'. {ex.GetBaseException().Message}", ex); }

	try 
	{ 
		serviceModelSessionToken = CreateServiceModelSessionToken(assemblies, modelIdentity, options.ServerHost, options.ModelPipePath);
		
		// Diagnostic: Check if ModelIdentity contains ModelLocation
		try
		{
			var logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RevitServerNet_ModelIdentity_Diagnostic.txt");
			var identityType = modelIdentity.GetType();
			var props = identityType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
			System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ModelIdentity properties:\n");
			foreach (var prop in props)
			{
				var val = prop.GetValue(modelIdentity);
				System.IO.File.AppendAllText(logPath, $"  {prop.Name}={val}\n");
			}
		}
		catch { }
	}
	catch (Exception ex) { throw new InvalidOperationException($"[ServiceModelSessionToken] Failed. Base='{assemblies.BaseDirectory}'. {ex.GetBaseException().Message}", ex); }

	// CRITICAL: НЕ оборачиваем LockData в try-catch, как в AltecSystems!
	// Если LockData падает, то весь export должен упасть!
	creationDate = LockData(assemblies, provider.proxyProvider, provider.iModelServiceType, provider.getBufferedProxy, provider.getStreamedProxy, serviceModelSessionToken, options.ServerHost);

		var tempDir = CreateTempModelDataFolder();
			try
			{
                var fileList = GetModelDataFileList(assemblies, provider.proxyProvider, provider.iModelServiceType, provider.getStreamedProxy, serviceModelSessionToken, options.ServerHost);
				await DownloadAllFilesAsync(assemblies, provider.proxyProvider, provider.iModelServiceType, provider.getStreamedProxy, serviceModelSessionToken, creationDate, fileList, tempDir, options.ServerHost, bytesProgress);

				GenerateRvtFromModelData(assemblies, tempDir, options.DestinationFile, options.Overwrite);
			}
			catch (Exception ex) { throw new InvalidOperationException($"[Download/Generate] Failed. Base='{assemblies.BaseDirectory}'. {ex.GetBaseException().Message}", ex); }
			finally
			{
				TryCleanupTemp(tempDir);
			}

			return options.DestinationFile;
		}

		private static void ValidateOptions(RsModelExporterOptions options)
		{
			if (options == null) throw new ArgumentNullException(nameof(options));
			if (string.IsNullOrWhiteSpace(options.ServerHost)) throw new ArgumentException("ServerHost is required", nameof(options.ServerHost));
			if (string.IsNullOrWhiteSpace(options.ModelPipePath)) throw new ArgumentException("ModelPipePath is required", nameof(options.ModelPipePath));
			if (string.IsNullOrWhiteSpace(options.DestinationFile)) throw new ArgumentException("DestinationFile is required", nameof(options.DestinationFile));
			if (string.IsNullOrWhiteSpace(options.RevitVersion)) throw new ArgumentException("RevitVersion is required", nameof(options.RevitVersion));
			var dir = Path.GetDirectoryName(options.DestinationFile);
			if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
			if (File.Exists(options.DestinationFile) && !options.Overwrite)
				throw new IOException("Destination file already exists. Set Overwrite=true to replace.");
		}

		private static (object proxyProviderInstance, Type iModelServiceType) CreateProxyProviderAndModelServiceType(
			RsAssemblies assemblies,
			string serverHost,
			string revitVersion,
			out MethodInfo getBufferedProxyMethod,
			out MethodInfo getStreamedProxyMethod)
		{
			var proxyProviderType = FindTypeAnyOrThrow(assemblies, "Autodesk.RevitServer.Enterprise.Common.ClientServer.Proxy.ProxyProvider");

			// Try multiple known creation paths for Provider across RS versions
			object provider = null;

			// Helper to validate a candidate provider instance
			bool IsValidProvider(object candidate)
			{
				if (candidate == null) return false;
				var t = candidate.GetType();
				var hasStream = t.GetMethods(BindingFlags.Public | BindingFlags.Instance).Any(m => m.Name == "GetStreamedProxy" && m.IsGenericMethodDefinition);
				var hasBuffered = t.GetMethods(BindingFlags.Public | BindingFlags.Instance).Any(m => m.Name == "GetBufferedProxy" && m.IsGenericMethodDefinition);
				var hasRouted = t.GetMethods(BindingFlags.Public | BindingFlags.Instance).Any(m => m.Name == "GetRoutedProxy" && m.IsGenericMethodDefinition);
				return hasStream || hasBuffered || hasRouted;
			}

			// 1) Known static factories
			var factories = new[] { "CreateProxyInstance", "CreateInstance", "Create", "GetInstance" };
			var failures = new List<Exception>();
			foreach (var name in factories)
			{
				var m1 = proxyProviderType.GetMethod(name, BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
				if (m1 != null)
				{
					try { provider = m1.Invoke(null, new object[] { revitVersion }); if (IsValidProvider(provider)) break; }
					catch (Exception ex) { failures.Add(new InvalidOperationException($"{proxyProviderType.FullName}.{name}(string) failed", ex)); provider = null; }
				}
				var m0 = proxyProviderType.GetMethod(name, BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
				if (provider == null && m0 != null)
				{
					try { provider = m0.Invoke(null, null); if (IsValidProvider(provider)) break; }
					catch (Exception ex) { failures.Add(new InvalidOperationException($"{proxyProviderType.FullName}.{name}() failed", ex)); provider = null; }
				}
			}

			// 2) Static properties
			if (provider == null)
			{
				foreach (var propName in new[] { "Instance", "Current", "Default" })
				{
					var p = proxyProviderType.GetProperty(propName, BindingFlags.Public | BindingFlags.Static);
					if (p != null)
					{
						try { provider = p.GetValue(null); if (IsValidProvider(provider)) break; } catch { provider = null; }
					}
				}
			}

			// 3) Constructors
			if (provider == null)
			{
				var ctor0 = proxyProviderType.GetConstructor(Type.EmptyTypes);
				if (ctor0 != null)
				{
					try { provider = ctor0.Invoke(null); if (!IsValidProvider(provider)) provider = null; }
					catch (Exception ex) { failures.Add(new InvalidOperationException($"{proxyProviderType.FullName}.ctor() failed", ex)); provider = null; }
				}
				var ctor1 = proxyProviderType.GetConstructor(new[] { typeof(string) });
				if (provider == null && ctor1 != null)
				{
					try { provider = ctor1.Invoke(new object[] { revitVersion }); if (!IsValidProvider(provider)) provider = null; }
					catch (Exception ex) { failures.Add(new InvalidOperationException($"{proxyProviderType.FullName}.ctor(string) failed", ex)); provider = null; }
				}
			}

			if (provider == null)
			{
				var hint = failures.Count > 0 ? failures[failures.Count - 1].GetBaseException().Message : "unknown error";
				throw new InvalidOperationException($"Failed to construct ProxyProvider for RS version '{revitVersion}'. Assemblies base='{assemblies.BaseDirectory}'. Last error: {hint}");
			}

			var iModelServiceType = FindTypeAnyOrThrow(assemblies, "Autodesk.RevitServer.Enterprise.Common.ClientServer.ServiceContract.Model.IModelService");
			getBufferedProxyMethod = FindGenericProxyMethodAny(provider.GetType(), new[] { "GetBufferedProxy" }, iModelServiceType);
			getStreamedProxyMethod = FindGenericProxyMethodAny(provider.GetType(), new[] { "GetStreamedProxy", "GetRoutedProxy" }, iModelServiceType);
			return (provider, iModelServiceType);
		}

		private static MethodInfo FindGenericProxyMethod(Type providerType, string methodName, Type serviceType)
		{
			var methods = providerType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
				.Where(m => m.Name == methodName && m.IsGenericMethodDefinition && m.GetParameters().Length == 1)
				.ToList();
			if (methods.Count == 0) throw new MissingMethodException(providerType.FullName, methodName);
			return methods[0].MakeGenericMethod(serviceType);
		}

		private static MethodInfo FindGenericProxyMethodAny(Type providerType, string[] candidateNames, Type serviceType)
		{
			foreach (var name in candidateNames)
			{
				var methods = providerType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
					.Where(m => m.Name == name && m.IsGenericMethodDefinition && m.GetParameters().Length == 1)
					.ToList();
				if (methods.Count > 0)
					return methods[0].MakeGenericMethod(serviceType);
			}
			throw new MissingMethodException(providerType.FullName, string.Join("|", candidateNames));
		}

		private static string CreateTempModelDataFolder()
		{
			var name = $"RevitServerNet_ModelData_{Guid.NewGuid():N}";
			var dir = Path.Combine(Path.GetTempPath(), name);
			Directory.CreateDirectory(dir);
			return dir;
		}

		private static object CreateServiceSessionToken(RsAssemblies assemblies)
		{
			var type = FindTypeAnyOrThrow(assemblies, "Autodesk.RevitServer.Enterprise.Common.ClientServer.DataContract.SessionToken.ServiceSessionToken");
			var ctor = type.GetConstructor(new[] { typeof(string), typeof(string), typeof(string), typeof(string) });
			if (ctor == null) throw new MissingMethodException(type.FullName, ".ctor(string,string,string,string)");
            // Use the same username pattern as the original tool for compatibility with RS
            var userName = $"RevitServerTool:{Environment.MachineName}:1";
			var token = ctor.Invoke(new object[] { userName, string.Empty, Environment.MachineName, Guid.NewGuid().ToString() });
			return token;
		}

	private static object CreateServiceModelSessionToken(RsAssemblies assemblies, object modelIdentity, string serverHost, string modelPipePath)
	{
		var type = FindTypeAnyOrThrow(assemblies, "Autodesk.RevitServer.Enterprise.Common.ClientServer.DataContract.SessionToken.ServiceModelSessionToken");
		var ctor = type.GetConstructor(new[] { FindTypeAnyOrThrow(assemblies, "Autodesk.RevitServer.Enterprise.Common.ClientServer.DataContract.Model.ModelIdentity"), typeof(string), typeof(string), typeof(string), typeof(string) });
		if (ctor == null) throw new MissingMethodException(type.FullName, ".ctor(ModelIdentity,string,string,string,string)");
            // Match Altec/Tool username pattern for model session
            var userName = $"RevitServerTool:{Environment.MachineName}:1";
		var token = ctor.Invoke(new object[] { modelIdentity, userName, string.Empty, Environment.MachineName, Guid.NewGuid().ToString() });
		// set ModelLocation property
		var modelLocation = CreateModelLocation(assemblies, serverHost, modelPipePath);
		var prop = type.GetProperty("ModelLocation", BindingFlags.Public | BindingFlags.Instance);
		if (prop != null)
		{
			try
			{
				var debugLog = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RevitServerNet_SessionToken_Debug.txt");
				System.IO.File.AppendAllText(debugLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ModelLocation property CanWrite={prop.CanWrite}\n");
				if (prop.CanWrite)
				{
					prop.SetValue(token, modelLocation);
					var checkVal = prop.GetValue(token);
					System.IO.File.AppendAllText(debugLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] After SetValue, ModelLocation={checkVal}\n");
				}
			}
			catch { }
		}
		return token;
	}

	// Create a new ServiceModelSessionToken from existing one (for file downloads)
	// AltecSystems creates a NEW token for EACH file download with a new operation GUID
	private static object CreateServiceModelSessionTokenFromExisting(RsAssemblies assemblies, object existingToken)
	{
		var type = existingToken.GetType();
		var modelIdentityProp = type.GetProperty("ModelIdentity", BindingFlags.Public | BindingFlags.Instance);
		var modelLocationProp = type.GetProperty("ModelLocation", BindingFlags.Public | BindingFlags.Instance);
		var userNameProp = type.GetProperty("UserName", BindingFlags.Public | BindingFlags.Instance);
		var ssoUserNameProp = type.GetProperty("SsoUserName", BindingFlags.Public | BindingFlags.Instance);
		var machineNameProp = type.GetProperty("MachineName", BindingFlags.Public | BindingFlags.Instance);
		
		var modelIdentity = modelIdentityProp?.GetValue(existingToken);
		var modelLocation = modelLocationProp?.GetValue(existingToken);
		var userName = userNameProp?.GetValue(existingToken) as string ?? $"RevitServerTool:{Environment.MachineName}:1";
		var ssoUserName = ssoUserNameProp?.GetValue(existingToken) as string ?? string.Empty;
		var machineName = machineNameProp?.GetValue(existingToken) as string ?? Environment.MachineName;
		
		var ctor = type.GetConstructor(new[] { FindTypeAnyOrThrow(assemblies, "Autodesk.RevitServer.Enterprise.Common.ClientServer.DataContract.Model.ModelIdentity"), typeof(string), typeof(string), typeof(string), typeof(string) });
		if (ctor == null) throw new MissingMethodException(type.FullName, ".ctor(ModelIdentity,string,string,string,string)");
		
		// Create new token with a NEW operation GUID (critical!)
		var newToken = ctor.Invoke(new object[] { modelIdentity, userName, ssoUserName, machineName, Guid.NewGuid().ToString() });
		
		// Copy ModelLocation
		var prop = type.GetProperty("ModelLocation", BindingFlags.Public | BindingFlags.Instance);
		if (prop != null && prop.CanWrite && modelLocation != null)
			prop.SetValue(newToken, modelLocation);
		
		return newToken;
	}

private static object CreateModelLocation(RsAssemblies assemblies, string serverHost, string modelPipePath)
{
	var type = FindTypeAnyOrThrow(assemblies, "Autodesk.RevitServer.Enterprise.Common.ClientServer.DataContract.Model.ModelLocation");
	var enumType = FindTypeAnyOrThrow(assemblies, "Autodesk.RevitServer.Enterprise.Common.ClientServer.DataContract.Model.ModelLocationType");
	var serverEnum = Enum.Parse(enumType, "Server");
	
	// CRITICAL: AltecSystems ВСЕГДА использует Windows-style path (e.g. "Base\In\test.rvt") для ModelLocation
	// Это ОБЯЗАТЕЛЬНОЕ условие для работы LockData!
	var path = ConvertPipePathToRelativeWindowsPath(modelPipePath);
	
	// CRITICAL: Используем параметризованный constructor, как в AltecSystems
	// new ModelLocation(HostIp, ModelPath, ModelLocationType.Server)
	var ctor = type.GetConstructor(new[] { typeof(string), typeof(string), enumType });
	if (ctor == null) throw new MissingMethodException(type.FullName, ".ctor(string,string,ModelLocationType)");
	object ml = ctor.Invoke(new object[] { serverHost, path, serverEnum });
	
	// CRITICAL: Явно установим Server, так как constructor его не устанавливает!
	try
	{
		var debugLog = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RevitServerNet_Server_Property_Debug.txt");
		System.IO.File.AppendAllText(debugLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] CreateModelLocation called\n");
	}
	catch { }
	
	// DEBUG: Выведем ВСЕ свойства ModelLocation
	try
	{
		var debugLog = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RevitServerNet_Server_Property_Debug.txt");
		var allProps = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
		System.IO.File.AppendAllText(debugLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ModelLocation has {allProps.Length} properties:\n");
		foreach (var p in allProps)
		{
			System.IO.File.AppendAllText(debugLog, $"  - {p.Name} ({p.PropertyType.Name}) CanWrite={p.CanWrite}\n");
		}
	}
	catch { }
	
	var serverProp = type.GetProperty("Server");
	try
	{
		var debugLog = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RevitServerNet_Server_Property_Debug.txt");
		System.IO.File.AppendAllText(debugLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] serverProp is {(serverProp == null ? "NULL" : "NOT NULL")}\n");
	}
	catch { }
	
	if (serverProp != null)
	{
		var canWrite = serverProp.CanWrite;
		try
		{
			var debugLog = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RevitServerNet_Server_Property_Debug.txt");
			System.IO.File.AppendAllText(debugLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Server property CanWrite={canWrite}\n");
			if (canWrite)
			{
				serverProp.SetValue(ml, serverHost);
				var checkVal = serverProp.GetValue(ml);
				System.IO.File.AppendAllText(debugLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] After SetValue('{serverHost}'), Server='{checkVal}'\n");
			}
			else
			{
				System.IO.File.AppendAllText(debugLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Server property is READ-ONLY!\n");
			}
		}
		catch (Exception ex)
		{
			try
			{
				var debugLog = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RevitServerNet_Server_Property_Debug.txt");
				System.IO.File.AppendAllText(debugLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] EXCEPTION: {ex.Message}\n");
			}
			catch { }
		}
	}
	
	// Диагностика: проверим, что создалось, включая CentralServer
	try
	{
		var logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RevitServerNet_ModelLocation_Diagnostic.txt");
		var serverPropForLog = type.GetProperty("Server");
		var centralServerProp = type.GetProperty("CentralServer");
		var pathProp = type.GetProperty("RelativePath");
		var typeProp = type.GetProperty("Type");
		var server = serverPropForLog?.GetValue(ml);
		var centralServer = centralServerProp?.GetValue(ml);
		var pathVal = pathProp?.GetValue(ml);
		var typeVal = typeProp?.GetValue(ml);
		System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ModelLocation created via ctor: Server={server}, CentralServer={centralServer}, RelativePath={pathVal}, Type={typeVal}, serverHost={serverHost}, path={path}\n");
	}
	catch { }
	
	return ml;
}

        private static object IdentifyModel(RsAssemblies assemblies, object proxyProvider, Type iModelServiceType, MethodInfo getStreamedProxy, object serviceSessionToken, string serverHost, string modelPipePath)
		{
        var clientProxy = getStreamedProxy.Invoke(proxyProvider, new object[] { serverHost });
		var proxy = GetProxyFromClientProxy(clientProxy);
		var identifyMethod = iModelServiceType.GetMethod("IdentifyModel", BindingFlags.Public | BindingFlags.Instance);
		if (identifyMethod == null) throw new MissingMethodException(iModelServiceType.FullName, "IdentifyModel");
            // Altec передаёт Windows-путь (замена | на \) для IdentifyModel
            var path = ConvertPipePathToRelativeWindowsPath(modelPipePath);
		try
		{
			var logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RevitServerNet_IdentifyModel_Diagnostic.txt");
			System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] IdentifyModel called: path='{path}', original='{modelPipePath}'\n");
		}
		catch { }
		var result = identifyMethod.Invoke(proxy, new object[] { serviceSessionToken, path, true });
		System.Diagnostics.Debug.WriteLine($"[IdentifyModel] result={result}");
		return result;
		}

	private static object LockData(RsAssemblies assemblies, object proxyProvider, Type iModelServiceType, MethodInfo getBufferedProxy, MethodInfo getStreamedProxy, object serviceModelSessionToken, string serverHost)
	{
		// CRITICAL: В ПРОПАТЧЕННОЙ DLL GetBufferedProxy() ВОЗВРАЩАЕТ GetStreamedProxy! (строка 155 в ExportModels.cs)
		// AltecSystems.GetBufferedProxy() => ProxyProvider.GetStreamedProxy<IModelService>()
		var clientProxy = getStreamedProxy.Invoke(proxyProvider, new object[] { serverHost });
		var proxy = GetProxyFromClientProxy(clientProxy);
			var lockData = iModelServiceType.GetMethod("LockData", BindingFlags.Public | BindingFlags.Instance);
			if (lockData == null) throw new MissingMethodException(iModelServiceType.FullName, "LockData");

			var modelVersionType = FindTypeAnyOrThrow(assemblies, "Autodesk.RevitServer.Enterprise.Common.ClientServer.DataContract.Model.ModelVersion");
			var versionNumberType = FindTypeAnyOrThrow(assemblies, "Autodesk.RevitServer.Enterprise.Common.ClientServer.DataContract.Model.VersionNumber");
			var historyCheckInfoType = FindTypeAnyOrThrow(assemblies, "Autodesk.RevitServer.Enterprise.Common.ClientServer.DataContract.Model.ModelHistoryCheckInfo");
			var episodeGuidType = FindTypeAnyOrThrow(assemblies, "Autodesk.RevitServer.Enterprise.Common.ClientServer.DataContract.Model.EpisodeGuid");

			var episodeInvalid = episodeGuidType.GetField("Invalid", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
			var historyCtor = historyCheckInfoType.GetConstructor(new[] { episodeGuidType });
			var history = historyCtor.Invoke(new[] { episodeInvalid });
			var versionNumberCtor = versionNumberType.GetConstructor(new[] { typeof(int) });
			var versionNumber = versionNumberCtor.Invoke(new object[] { 0 });
			var modelVersionCtor = modelVersionType.GetConstructor(new[] { versionNumberType, historyCheckInfoType });
			var modelVersion = modelVersionCtor.Invoke(new[] { versionNumber, history });

	// Prepare args with out parameter
	var args = new object[] { serviceModelSessionToken, (uint)129, true, modelVersion, null };
	
	// ДЕТАЛЬНАЯ ДИАГНОСТИКА: вывести ВСЕ параметры LockData в файл
	try
	{
		var logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RevitServerNet_LockData_Diagnostic.txt");
		using (var sw = new System.IO.StreamWriter(logPath, true, System.Text.Encoding.UTF8))
		{
			sw.WriteLine($"\n========== LockData called at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==========");
			sw.WriteLine($"ServerHost: {serverHost}");
			sw.WriteLine($"LockOptions: 129");
			sw.WriteLine($"AllowNonExclusive: true");
			sw.WriteLine($"ModelVersion: {modelVersion}");
			sw.WriteLine($"SessionToken Type: {serviceModelSessionToken.GetType().FullName}");
			
			var tokenType = serviceModelSessionToken.GetType();
			
			// Dump ALL properties of ServiceModelSessionToken
			sw.WriteLine("\n--- ServiceModelSessionToken Properties ---");
			var allProps = tokenType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
			foreach (var prop in allProps)
			{
				try
				{
					var val = prop.GetValue(serviceModelSessionToken);
					sw.WriteLine($"  {prop.Name} ({prop.PropertyType.Name}): {val}");
					
					// Special handling for ModelLocation
					if (prop.Name == "ModelLocation" && val != null)
					{
						sw.WriteLine("    --- ModelLocation Details ---");
						var mlType = val.GetType();
						var mlProps = mlType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
						foreach (var mlProp in mlProps)
						{
							try
							{
								var mlVal = mlProp.GetValue(val);
								sw.WriteLine($"      {mlProp.Name}: {mlVal}");
							}
							catch (Exception ex)
							{
								sw.WriteLine($"      {mlProp.Name}: ERROR - {ex.Message}");
							}
						}
					}
					
					// Special handling for ModelIdentity
					if (prop.Name == "ModelIdentity" && val != null)
					{
						sw.WriteLine("    --- ModelIdentity Details ---");
						var miType = val.GetType();
						var miProps = miType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
						foreach (var miProp in miProps)
						{
							try
							{
								var miVal = miProp.GetValue(val);
								sw.WriteLine($"      {miProp.Name}: {miVal}");
							}
							catch (Exception ex)
							{
								sw.WriteLine($"      {miProp.Name}: ERROR - {ex.Message}");
							}
						}
					}
				}
				catch (Exception ex)
				{
					sw.WriteLine($"  {prop.Name}: ERROR - {ex.Message}");
				}
			}
			
			sw.WriteLine("========================================\n");
		}
	}
	catch (Exception ex)
	{
		Console.WriteLine($"[DIAGNOSTIC ERROR] {ex.Message}");
	}
		
	try
	{
		lockData.Invoke(proxy, args);
	}
	catch (TargetInvocationException tie) when (tie.InnerException is FaultException faultEx)
	{
		// ДЕТАЛЬНОЕ логирование ServiceFault
		var logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RevitServerNet_LockData_Fault.txt");
		try
		{
			using (var sw = new System.IO.StreamWriter(logPath, true, System.Text.Encoding.UTF8))
			{
				sw.WriteLine($"\n========== LockData Fault at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==========");
				sw.WriteLine($"FaultException Type: {faultEx.GetType().FullName}");
				sw.WriteLine($"Message: {faultEx.Message}");
				
				var detailProp = faultEx.GetType().GetProperty("Detail");
				var detail = detailProp?.GetValue(faultEx);
				if (detail != null)
				{
					sw.WriteLine($"Detail Type: {detail.GetType().FullName}");
					sw.WriteLine($"Detail: {detail}");
					
					// Dump all properties of ServiceFault
					var detailType = detail.GetType();
					var detailProps = detailType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
					sw.WriteLine("\n--- ServiceFault Properties ---");
					foreach (var prop in detailProps)
					{
						try
						{
							var val = prop.GetValue(detail);
							sw.WriteLine($"  {prop.Name}: {val}");
						}
						catch (Exception ex)
						{
							sw.WriteLine($"  {prop.Name}: ERROR - {ex.Message}");
						}
					}
				}
				sw.WriteLine("========================================\n");
			}
		}
		catch { }
		
		var detailProp2 = faultEx.GetType().GetProperty("Detail");
		var detail2 = detailProp2?.GetValue(faultEx);
		string detailStr = detail2 != null ? $" Detail={detail2}" : "";
		throw new InvalidOperationException($"Server Fault: {faultEx.Message}{detailStr}", faultEx);
	}
		return args[4]; // creationDate (EpisodeGuid)
		}

        private static IEnumerable<string> GetModelDataFileList(RsAssemblies assemblies, object proxyProvider, Type iModelServiceType, MethodInfo getStreamedProxy, object serviceModelSessionToken, string serverHost)
		{
            var clientProxy = getStreamedProxy.Invoke(proxyProvider, new object[] { serverHost });
			var proxy = GetProxyFromClientProxy(clientProxy);
			var method = iModelServiceType.GetMethod("GetListOfModelDataFilesWithoutLocking", BindingFlags.Public | BindingFlags.Instance);
			if (method == null) throw new MissingMethodException(iModelServiceType.FullName, "GetListOfModelDataFilesWithoutLocking");
			var args = new object[] { serviceModelSessionToken, null };
			method.Invoke(proxy, args);
			var list = args[1] as IEnumerable;
			if (list == null) return Enumerable.Empty<string>();
			var result = new List<string>();
			foreach (var x in list) if (x != null) result.Add(x.ToString());
			return result;
		}

		private static async Task DownloadAllFilesAsync(
			RsAssemblies assemblies,
			object proxyProvider,
			Type iModelServiceType,
			MethodInfo getStreamedProxy,
			object serviceModelSessionToken,
			object creationDate,
			IEnumerable<string> fileList,
			string tempDir,
			string serverHost,
			IProgress<long> progress)
		{
		var clientProxy = getStreamedProxy.Invoke(proxyProvider, new object[] { serverHost });
		var proxy = GetProxyFromClientProxy(clientProxy);

	// FileDownloadRequestMessage and FileDownloadMessageStream are in Autodesk.Social namespace, not in Enterprise.Common
	// Try to find FileDownloadRequestMessage - it might be in a different namespace
	Type requestType = null;
	try
	{
		requestType = FindTypeAnyOrThrow(assemblies, "Autodesk.Social.Services.Files.ServiceContracts.FileDownloadRequestMessage");
	}
	catch
	{
		// Try alternative namespace
		try
		{
			requestType = FindTypeAnyOrThrow(assemblies, "Autodesk.RevitServer.Enterprise.Common.ClientServer.DataContract.Message.FileDownloadRequestMessage");
		}
		catch
		{
		// Try to find any type with this name in all assemblies
		var dllNames = new[] { "Autodesk.RevitServer.Social.dll", "RS.Enterprise.Common.ClientServer.DataContract.dll", 
			"RS.Enterprise.Common.ClientServer.Helper.dll", "RS.Enterprise.Common.ClientServer.Proxy.dll",
			"RS.Enterprise.Common.ClientServer.ServiceContract.Local.dll", "RS.Enterprise.Common.ClientServer.ServiceContract.Model.dll" };
		foreach (var dllName in dllNames)
		{
			var asm = assemblies.FindAssembly(dllName);
			if (asm != null)
			{
				requestType = asm.GetTypes().FirstOrDefault(t => t.Name == "FileDownloadRequestMessage");
				if (requestType != null) break;
			}
		}
		if (requestType == null)
			throw new TypeLoadException("Cannot find FileDownloadRequestMessage in any assembly");
		}
	}
	var messageStreamType = FindTypeAnyOrThrow(assemblies, "Autodesk.Social.Services.Files.ServiceContracts.FileDownloadMessageStream");
		var downloadMethod = iModelServiceType.GetMethod("DownloadFile", BindingFlags.Public | BindingFlags.Instance, null, new[] { requestType }, null);
			if (downloadMethod == null) throw new MissingMethodException(iModelServiceType.FullName, "DownloadFile");

			var requestCtor = requestType.GetConstructor(new[] { FindTypeAnyOrThrow(assemblies, "Autodesk.RevitServer.Enterprise.Common.ClientServer.DataContract.SessionToken.ServiceModelSessionToken"), FindTypeAnyOrThrow(assemblies, "Autodesk.RevitServer.Enterprise.Common.ClientServer.DataContract.Model.EpisodeGuid"), typeof(string) });
			if (requestCtor == null) throw new MissingMethodException(requestType.FullName, ".ctor(ServiceModelSessionToken,EpisodeGuid,string)");

			var streamProp = messageStreamType.GetProperty("Stream", BindingFlags.Public | BindingFlags.Instance);
			if (streamProp == null) throw new MissingMemberException(messageStreamType.FullName, "Stream");

		foreach (var file in fileList)
		{
			// CRITICAL: Create a NEW ServiceModelSessionToken for EACH file (as AltecSystems does)
			// This is required because the token might contain state that changes between downloads
			var tokenForFile = CreateServiceModelSessionTokenFromExisting(assemblies, serviceModelSessionToken);
			var sourceFileName = GetSourceFileName(assemblies, serviceModelSessionToken, file);
			
			// Diagnostic logging
			try
			{
				var logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RevitServerNet_DownloadFile_Diagnostic.txt");
				var tokenType = tokenForFile.GetType();
				var mlProp = tokenType.GetProperty("ModelLocation");
				var mlVal = mlProp?.GetValue(tokenForFile);
				System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] DownloadFile: file={file}, sourceFileName={sourceFileName}, creationDate={creationDate}, ModelLocation={mlVal}\n");
			}
			catch { }
			
			var request = requestCtor.Invoke(new[] { tokenForFile, creationDate, sourceFileName });
			var msg = downloadMethod.Invoke(proxy, new[] { request });
			if (msg == null) throw new InvalidOperationException("DownloadFile returned null message");
			using (var stream = (Stream)streamProp.GetValue(msg))
			{
				if (stream == null) throw new InvalidOperationException("DownloadFile returned null stream");
				var target = Path.Combine(tempDir, Path.GetFileName(file));
				Directory.CreateDirectory(Path.GetDirectoryName(target));
				await CopyToFileAsync(stream, target, progress);
			}
		}
		}

		private static object GetSourceFileName(RsAssemblies assemblies, object serviceModelSessionToken, string fileName)
		{
			var tokenType = serviceModelSessionToken.GetType();
			var identityProp = tokenType.GetProperty("ModelIdentity", BindingFlags.Public | BindingFlags.Instance);
			var modelIdentity = identityProp?.GetValue(serviceModelSessionToken);
			var identityType = FindTypeAnyOrThrow(assemblies, "Autodesk.RevitServer.Enterprise.Common.ClientServer.DataContract.Model.ModelIdentity");
			var guidProp = identityType.GetProperty("IdentityGUID", BindingFlags.Public | BindingFlags.Instance);
			var guidValue = guidProp?.GetValue(modelIdentity);
			var guidValueType = FindTypeAnyOrThrow(assemblies, "Autodesk.RevitServer.Enterprise.Common.ClientServer.DataContract.Model.GUIDValue");
			var guidInnerProp = guidValueType.GetProperty("GUID", BindingFlags.Public | BindingFlags.Instance);
			var guid = (Guid)guidInnerProp.GetValue(guidValue);
			var combined = Path.Combine(guid.ToString(), fileName);
			return combined;
		}

		private static async Task CopyToFileAsync(Stream source, string targetPath, IProgress<long> progress)
		{
			const int BufferSize = 16384;
			using (var file = File.Open(targetPath, FileMode.Create, FileAccess.Write, FileShare.None))
			{
				var buffer = new byte[BufferSize];
				int read;
				long total = 0;
				while ((read = await source.ReadAsync(buffer, 0, buffer.Length)) > 0)
				{
					await file.WriteAsync(buffer, 0, read);
					total += read;
					progress?.Report(total);
				}
			}
		}

		private static void GenerateRvtFromModelData(RsAssemblies assemblies, string modelDataDir, string destinationFile, bool overwrite)
		{
			if (File.Exists(destinationFile))
			{
				if (!overwrite) throw new IOException("Destination file exists and overwrite is false");
				File.Delete(destinationFile);
			}

			var dataFormatVersionType = FindTypeAnyOrThrow(assemblies, "Autodesk.RevitServer.Enterprise.Common.ClientServer.DataContract.Model.DataFormatVersion");
			var latest = Enum.Parse(dataFormatVersionType, "Latest");

			var versionMgrType = FindTypeAnyOrThrow(assemblies, "Autodesk.RevitServer.Enterprise.Common.ClientServer.Helper.VersionManager.ModelDataVersionManager");
			var iVersionMgrType = FindTypeAnyOrThrow(assemblies, "Autodesk.RevitServer.Enterprise.Common.ClientServer.Helper.VersionManager.IModelDataVersionManager");
			var sharedUtilsType = FindTypeAnyOrThrow(assemblies, "Autodesk.RevitServer.Enterprise.Common.ClientServer.Helper.Utils.SharedUtils");
			var modelPathUtilsProp = sharedUtilsType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
			var sharedUtilsInstance = modelPathUtilsProp?.GetValue(null);
			var innerPathUtilsProp = sharedUtilsType.GetProperty("ModelPathUtils", BindingFlags.Public | BindingFlags.Instance);

			var versionMgr = Activator.CreateInstance(versionMgrType, new object[] { modelDataDir, latest });
			var setModelPathUtils = iVersionMgrType.GetProperty("ModelPathUtils", BindingFlags.Public | BindingFlags.Instance);
			if (setModelPathUtils != null && innerPathUtilsProp != null && sharedUtilsInstance != null)
			{
				var utils = innerPathUtilsProp.GetValue(sharedUtilsInstance);
				setModelPathUtils.SetValue(versionMgr, utils);
			}

			var dictStringString = typeof(Dictionary<,>).MakeGenericType(typeof(string), typeof(string));
			var dictIntString = typeof(Dictionary<,>).MakeGenericType(typeof(int), typeof(string));
			var nonElem = Activator.CreateInstance(dictStringString);
			var elem = Activator.CreateInstance(dictIntString);
			var steel = Activator.CreateInstance(dictIntString);

			var getLatestStreamFiles = iVersionMgrType.GetMethod("GetLatestStreamFiles", BindingFlags.Public | BindingFlags.Instance);
			var args = new object[] { nonElem, elem, steel };
			var ok = (bool)getLatestStreamFiles.Invoke(versionMgr, args);
			if (!ok) throw new InvalidOperationException("GetLatestStreamFiles returned false");

			var rvtFileType = FindTypeAnyOrThrow(assemblies, "Autodesk.RevitServer.Enterprise.Common.ClientServer.Helper.OleFile.RvtFile");
			var gen = rvtFileType.GetMethod("GenerateRvtFileFromModelFolder", BindingFlags.Public | BindingFlags.Static);
			var success = (bool)gen.Invoke(null, new object[] { args[0], args[1], args[2], latest, destinationFile });
			if (!success) throw new InvalidOperationException("Failed to generate RVT from model data folder");
		}

		private static object GetProxyFromClientProxy(object clientProxy)
		{
			if (clientProxy == null) throw new ArgumentNullException(nameof(clientProxy));
			var prop = clientProxy.GetType().GetProperty("Proxy", BindingFlags.Public | BindingFlags.Instance);
			if (prop == null) throw new MissingMemberException(clientProxy.GetType().FullName, "Proxy");
			return prop.GetValue(clientProxy);
		}

		private static string GetHostFromModelPipePath(string modelPipePath)
		{
			// When using ProxyProvider.Get*Proxy(host), pass server host. Caller will supply host when available.
			// Here we return null to use external parameter through delegates above, so this method is kept for completeness.
			return null;
		}

        private static string ConvertPipePathToRelativeWindowsPath(string pipePath)
		{
			if (string.IsNullOrWhiteSpace(pipePath)) return pipePath;
			var p = pipePath.Trim();
			if (p.StartsWith("|")) p = p.Substring(1);
            return p.Replace('|', Path.DirectorySeparatorChar);
		}

        private static string ConvertPipePathToRelativePipePath(string pipePath)
        {
            if (string.IsNullOrWhiteSpace(pipePath)) return pipePath;
            var p = pipePath.Trim();
            if (p.StartsWith("|")) p = p.Substring(1);
            return p; // keep '|' delimiters
        }

		private static void TryCleanupTemp(string dir)
		{
			try { if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
		}

		private static Type FindTypeOrThrow(RsAssemblies assemblies, string fullName)
		{
			var t = assemblies.GetType(fullName);
			if (t == null) throw new TypeLoadException($"Type not found: {fullName}");
			return t;
		}

		private static Type FindTypeAnyOrThrow(RsAssemblies assemblies, string autodeskFullName)
		{
			// Try Autodesk.* namespace first (older samples) then RS.* (packaged/in newer distros)
			var t = assemblies.GetType(autodeskFullName);
			if (t != null) return t;
			var rsName = autodeskFullName.Replace("Autodesk.RevitServer.", "RS.");
			t = assemblies.GetType(rsName);
			if (t != null) return t;
			var rsEnterpriseName = autodeskFullName.Replace("Autodesk.RevitServer.Enterprise.Common.ClientServer", "RS.Enterprise.Common.ClientServer");
			t = assemblies.GetType(rsEnterpriseName);
			if (t != null) return t;
			throw new TypeLoadException($"Type not found: {autodeskFullName}");
		}
	}
}


