using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
			try
			{
				ValidateOptions(options);

				var assemblies = RsAssemblyLoader.Load(options.RevitVersion, options.AssembliesPath);
				bool UseEnvFlag(string name)
				{
					try { return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)); } catch { return false; }
				}


				object serviceSessionToken;
				object modelIdentity;
				object serviceModelSessionToken;
				object creationDate;
				(string stage, Exception error) Fail(string stage, Exception ex) => (stage, ex);

				// No longer need to initialize proxy provider here - each method creates its own

				try { serviceSessionToken = CreateServiceSessionToken(assemblies); }
				catch (Exception ex) { throw new InvalidOperationException($"[ServiceSessionToken] Failed to create. Base='{assemblies.BaseDirectory}'. {ex.GetBaseException().Message}", ex); }

				try { modelIdentity = IdentifyModel(assemblies, options.RevitVersion, options.ServerHost, serviceSessionToken, options.ModelPipePath); }
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

				// Acquire lock (must succeed as in Altec)
				creationDate = LockData(assemblies, options.RevitVersion, options.ServerHost, serviceModelSessionToken);

				var tempDir = CreateTempModelDataFolder();
				try
				{
					var fileList = GetModelDataFileList(assemblies, options.RevitVersion, options.ServerHost, serviceModelSessionToken);
					await DownloadAllFilesAsync(assemblies, options.RevitVersion, options.ServerHost, serviceModelSessionToken, creationDate, fileList, tempDir, bytesProgress);

					GenerateRvtFromModelData(assemblies, tempDir, options.DestinationFile, options.Overwrite);
				}
				catch (Exception ex) { throw new InvalidOperationException($"[Download/Generate] Failed. Base='{assemblies.BaseDirectory}'. {ex.GetBaseException().Message}", ex); }
				finally
				{
					TryCleanupTemp(tempDir);
				}

				return options.DestinationFile;
			}
			catch (AmbiguousMatchException ex)
			{
				// Debug: Log which method is causing ambiguity
				var logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RevitServerNet_ExportAsync_Ambiguity.txt");
				System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ExportAsync ambiguity: {ex.Message}\n");
				System.IO.File.AppendAllText(logPath, $"Stack trace: {ex.StackTrace}\n");
				throw new InvalidOperationException($"ExportAsync ambiguity: {ex.Message}", ex);
			}
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
			try
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
			catch (AmbiguousMatchException ex)
			{
				// Debug: Log which property is causing ambiguity
				var logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RevitServerNet_CreateServiceModelSessionToken_Ambiguity.txt");
				System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] CreateServiceModelSessionTokenFromExisting ambiguity: {ex.Message}\n");
				System.IO.File.AppendAllText(logPath, $"Stack trace: {ex.StackTrace}\n");
				throw new InvalidOperationException($"CreateServiceModelSessionTokenFromExisting ambiguity: {ex.Message}", ex);
			}
		}

		private static object CreateModelLocation(RsAssemblies assemblies, string serverHost, string modelPipePath)
		{
			var type = FindTypeAnyOrThrow(assemblies, "Autodesk.RevitServer.Enterprise.Common.ClientServer.DataContract.Model.ModelLocation");
			var enumType = FindTypeAnyOrThrow(assemblies, "Autodesk.RevitServer.Enterprise.Common.ClientServer.DataContract.Model.ModelLocationType");
			var serverEnum = Enum.Parse(enumType, "Server");

			// CRITICAL: AltecSystems ВСЕГДА использует Windows-style path (e.g. "Base\In\test.rvt") для ModelLocation
			// Это ОБЯЗАТЕЛЬНОЕ условие для работы LockData!
			var path = ConvertPipePathToRelativeWindowsPath(modelPipePath);
			
			// DEBUG: Log what we're passing to constructor
			try
			{
				var logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RevitServerNet_ModelLocation_Diagnostic.txt");
				System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] CreateModelLocation constructor args:\n");
				System.IO.File.AppendAllText(logPath, $"  serverHost='{serverHost}'\n");
				System.IO.File.AppendAllText(logPath, $"  path='{path}'\n");
				System.IO.File.AppendAllText(logPath, $"  originalPipePath='{modelPipePath}'\n");
				System.IO.File.AppendAllText(logPath, $"  serverEnum={serverEnum}\n");
			}
			catch { }

			// DEBUG: List all available constructors
			try
			{
				var logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RevitServerNet_ModelLocation_Constructors.txt");
				var constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
				System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ModelLocation constructors ({constructors.Length}):\n");
				foreach (var ctor in constructors)
				{
					var paramTypes = ctor.GetParameters().Select(p => p.ParameterType.Name).ToArray();
					System.IO.File.AppendAllText(logPath, $"  - {string.Join(", ", paramTypes)}\n");
				}
			}
			catch { }

			// CRITICAL: Use EXACTLY the same constructor as AltecSystems
			// AltecSystems: new ModelLocation(HostIp, ModelPath, ModelLocationType.Server)
			// This MUST be the 3-parameter constructor that sets Server field internally
			var ctor3 = type.GetConstructor(new[] { typeof(string), typeof(string), enumType });
			if (ctor3 == null)
			{
				throw new MissingMethodException(type.FullName, "Constructor(string, string, ModelLocationType) not found - this is required for AltecSystems compatibility");
			}
			
			// Use correct parameter order: (serverHost, path, serverEnum) as per AltecSystems
			var ml = ctor3.Invoke(new object[] { serverHost, path, serverEnum });
			
			// CRITICAL: Try to set Server field after construction (patched DLL might not set it in constructor)
			try
			{
				var serverProp = type.GetProperty("Server");
				if (serverProp != null && serverProp.CanWrite)
				{
					serverProp.SetValue(ml, serverHost);
				}
				else
				{
					// Try to find Server field
					var serverField = type.GetField("Server", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
					if (serverField != null)
					{
						serverField.SetValue(ml, serverHost);
					}
				}
			}
			catch { }
			
			// DEBUG: Log the created ModelLocation to verify it's correct
			try
			{
				var logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RevitServerNet_ModelLocation_Diagnostic.txt");
				var serverProp = type.GetProperty("Server");
				var centralServerProp = type.GetProperty("CentralServer");
				var relativePathProp = type.GetProperty("RelativePath");
				var typeProp = type.GetProperty("Type");
				
				var serverVal = serverProp?.GetValue(ml);
				var centralServerVal = centralServerProp?.GetValue(ml);
				var relativePathVal = relativePathProp?.GetValue(ml);
				var typeVal = typeProp?.GetValue(ml);
				
				System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ModelLocation created via ctor: Server={serverVal}, CentralServer={centralServerVal}, RelativePath={relativePathVal}, Type={typeVal}, serverHost={serverHost}, path={path}\n");
				
				// DEBUG: List ALL properties to see what's available
				var allProps = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
				System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] All ModelLocation properties ({allProps.Length}):\n");
				foreach (var prop in allProps)
				{
					var val = prop.GetValue(ml);
					System.IO.File.AppendAllText(logPath, $"  - {prop.Name} ({prop.PropertyType.Name}): {val}\n");
				}
			}
			catch { }

			return ml;
		}

		private static object IdentifyModel(RsAssemblies assemblies, String revitVersion, String serverHost, Object serviceSessionToken, string modelPipePath)
		{
			var clientProxy = GetGenericClientProxy(assemblies, revitVersion, serverHost, useStreamed: false);
			var proxy = GetProxyFromClientProxy(clientProxy);
			var iModelServiceType = assemblies.GetType("Autodesk.RevitServer.Enterprise.Common.ClientServer.ServiceContract.Model.IModelService");
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

		private static object LockData(RsAssemblies assemblies, String revitVersion, String serverHost, Object serviceModelSessionToken)
		{
			var clientProxy = GetGenericClientProxy(assemblies, revitVersion, serverHost, useStreamed: false);
			var proxy = GetProxyFromClientProxy(clientProxy);
			var iModelServiceType = assemblies.GetType("Autodesk.RevitServer.Enterprise.Common.ClientServer.ServiceContract.Model.IModelService");
			var lockData = iModelServiceType.GetMethod("LockData", BindingFlags.Public | BindingFlags.Instance);
			if (lockData == null) throw new MissingMethodException(iModelServiceType.FullName, "LockData");

			var modelVersionType = FindTypeAnyOrThrow(assemblies, "Autodesk.RevitServer.Enterprise.Common.ClientServer.DataContract.Model.ModelVersion");
			var versionNumberType = FindTypeAnyOrThrow(assemblies, "Autodesk.RevitServer.Enterprise.Common.ClientServer.DataContract.Model.VersionNumber");
			var historyCheckInfoType = FindTypeAnyOrThrow(assemblies, "Autodesk.RevitServer.Enterprise.Common.ClientServer.DataContract.Model.ModelHistoryCheckInfo");
			var episodeGuidType = FindTypeAnyOrThrow(assemblies, "Autodesk.RevitServer.Enterprise.Common.ClientServer.DataContract.Model.EpisodeGuid");

			// DEBUG: List all available fields and properties on EpisodeGuid
			try
			{
				var logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RevitServerNet_EpisodeGuid_Debug.txt");
				var fields = episodeGuidType.GetFields(BindingFlags.Public | BindingFlags.Static);
				var properties = episodeGuidType.GetProperties(BindingFlags.Public | BindingFlags.Static);
				System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] EpisodeGuid fields ({fields.Length}):\n");
				foreach (var field in fields)
				{
					var val = field.GetValue(null);
					System.IO.File.AppendAllText(logPath, $"  - {field.Name} ({field.FieldType.Name}): {val}\n");
				}
				System.IO.File.AppendAllText(logPath, $"EpisodeGuid properties ({properties.Length}):\n");
				foreach (var prop in properties)
				{
					try
					{
						var val = prop.GetValue(null);
						System.IO.File.AppendAllText(logPath, $"  - {prop.Name} ({prop.PropertyType.Name}): {val}\n");
					}
					catch (Exception ex)
					{
						System.IO.File.AppendAllText(logPath, $"  - {prop.Name} ({prop.PropertyType.Name}): ERROR - {ex.Message}\n");
					}
				}
			}
			catch { }

			var episodeInvalid = episodeGuidType.GetProperty("Invalid", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
			if (episodeInvalid == null) throw new InvalidOperationException("EpisodeGuid.Invalid property is null");
			
			var historyCtor = historyCheckInfoType.GetConstructor(new[] { episodeGuidType });
			if (historyCtor == null) throw new MissingMethodException(historyCheckInfoType.FullName, "Constructor(EpisodeGuid)");
			var history = historyCtor.Invoke(new[] { episodeInvalid });
			if (history == null) throw new InvalidOperationException("ModelHistoryCheckInfo creation failed");
			
			var versionNumberCtor = versionNumberType.GetConstructor(new[] { typeof(int) });
			if (versionNumberCtor == null) throw new MissingMethodException(versionNumberType.FullName, "Constructor(int)");
			var versionNumber = versionNumberCtor.Invoke(new object[] { 0 });
			if (versionNumber == null) throw new InvalidOperationException("VersionNumber creation failed");
			
			var modelVersionCtor = modelVersionType.GetConstructor(new[] { versionNumberType, historyCheckInfoType });
			if (modelVersionCtor == null) throw new MissingMethodException(modelVersionType.FullName, "Constructor(VersionNumber, ModelHistoryCheckInfo)");
			var modelVersion = modelVersionCtor.Invoke(new[] { versionNumber, history });
			if (modelVersion == null) throw new InvalidOperationException("ModelVersion creation failed");

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

		private static IEnumerable<string> GetModelDataFileList(RsAssemblies assemblies, String revitVersion, String serverHost, Object serviceModelSessionToken)
		{
			var clientProxy = GetGenericClientProxy(assemblies, revitVersion, serverHost, useStreamed: false);
			var proxy = GetProxyFromClientProxy(clientProxy);
			var iModelServiceType = assemblies.GetType("Autodesk.RevitServer.Enterprise.Common.ClientServer.ServiceContract.Model.IModelService");
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
			String revitVersion,
			String serverHost,
			Object serviceModelSessionToken,
			object creationDate,
			IEnumerable<string> fileList,
			string tempDir,
			IProgress<long> progress)
		{
			var clientProxy = GetGenericClientProxy(assemblies, revitVersion, serverHost, useStreamed: true);
			var proxy = GetProxyFromClientProxy(clientProxy);
			var iModelServiceType = assemblies.GetType("Autodesk.RevitServer.Enterprise.Common.ClientServer.ServiceContract.Model.IModelService");
		// Resolve FileDownloadRequestMessage from available assemblies
		Type requestType;
		try
		{
			requestType = ResolveTypeAny(assemblies, new[] {
		"Autodesk.Social.Services.Files.ServiceContracts.FileDownloadRequestMessage",
		"Autodesk.RevitServer.Enterprise.Common.ClientServer.DataContract.Message.FileDownloadRequestMessage"
	}) ?? throw new TypeLoadException("Cannot find FileDownloadRequestMessage");
		}
		catch (AmbiguousMatchException ex)
		{
			// Debug: Log which type is causing ambiguity
			var logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RevitServerNet_ResolveTypeAny_Ambiguity.txt");
			System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ResolveTypeAny ambiguity: {ex.Message}\n");
			System.IO.File.AppendAllText(logPath, $"Stack trace: {ex.StackTrace}\n");
			throw new InvalidOperationException($"ResolveTypeAny ambiguity: {ex.Message}", ex);
		}
		
		// Find DownloadFile method with specific parameter type to avoid ambiguity
		MethodInfo downloadMethod = null;
		try
		{
			downloadMethod = iModelServiceType.GetMethod("DownloadFile", BindingFlags.Public | BindingFlags.Instance, null, new[] { requestType }, null);
			if (downloadMethod == null)
			{
				// Try to find the method with more specific signature
				var methods = iModelServiceType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
					.Where(m => m.Name == "DownloadFile" && m.GetParameters().Length == 1)
					.ToArray();
				if (methods.Length == 1)
				{
					downloadMethod = methods[0];
				}
				else if (methods.Length > 1)
				{
					// Find the one that takes the correct parameter type
					downloadMethod = methods.FirstOrDefault(m => m.GetParameters()[0].ParameterType == requestType);
				}
			}
			if (downloadMethod == null) throw new MissingMethodException(iModelServiceType.FullName, "DownloadFile");
		}
		catch (AmbiguousMatchException ex)
		{
			// Debug: List all DownloadFile methods to see the ambiguity
			var logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RevitServerNet_DownloadFile_Methods.txt");
			var methods = iModelServiceType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
				.Where(m => m.Name == "DownloadFile")
				.ToArray();
			System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] DownloadFile methods ({methods.Length}):\n");
			foreach (var method in methods)
			{
				var paramTypes = method.GetParameters().Select(p => p.ParameterType.Name).ToArray();
				System.IO.File.AppendAllText(logPath, $"  - {string.Join(", ", paramTypes)}\n");
			}
			throw new InvalidOperationException($"Ambiguous DownloadFile method: {ex.Message}", ex);
		}

			var serviceModelSessionTokenType = FindTypeAnyOrThrow(assemblies, "Autodesk.RevitServer.Enterprise.Common.ClientServer.DataContract.SessionToken.ServiceModelSessionToken");
			var episodeGuidType = FindTypeAnyOrThrow(assemblies, "Autodesk.RevitServer.Enterprise.Common.ClientServer.DataContract.Model.EpisodeGuid");
			var requestCtor = requestType.GetConstructor(new[] { serviceModelSessionTokenType, episodeGuidType, typeof(string) });
			if (requestCtor == null) throw new MissingMethodException(requestType.FullName, ".ctor(ServiceModelSessionToken,EpisodeGuid,string)");

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
				using (var stream = ExtractStreamFromMessage(msg))
				{
					if (stream == null)
					{
						try
						{
							var dbg = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RevitServerNet_DownloadFile_MessageType.txt");
							var t = msg.GetType();
							using (var sw = new System.IO.StreamWriter(dbg, true, System.Text.Encoding.UTF8))
							{
								sw.WriteLine($"\n==== {DateTime.Now:yyyy-MM-dd HH:mm:ss} Message type dump ====");
								sw.WriteLine($"Type: {t.FullName}");
								try { sw.WriteLine($"Assembly: {t.Assembly.Location}"); } catch { }
								sw.WriteLine("Properties:");
								foreach (var p in t.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance))
								{
									sw.WriteLine($"  - {p.Name} : {p.PropertyType.FullName}");
								}
								sw.WriteLine("Fields:");
								foreach (var f in t.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance))
								{
									sw.WriteLine($"  - {f.Name} : {f.FieldType.FullName}");
								}
							}
						}
						catch { }
						throw new InvalidOperationException("DownloadFile returned null stream");
					}
					var target = Path.Combine(tempDir, Path.GetFileName(file));
					Directory.CreateDirectory(Path.GetDirectoryName(target));
					await CopyToFileAsync(stream, target, progress);
				}
			}
		}

		private static object GetSourceFileName(RsAssemblies assemblies, object serviceModelSessionToken, string fileName)
		{
			try
			{
				var tokenType = serviceModelSessionToken.GetType();
				PropertyInfo identityProp = null;
				try { identityProp = tokenType.GetProperty("ModelIdentity", BindingFlags.Public | BindingFlags.Instance); } catch (AmbiguousMatchException ex) { LogAmbiguity("GetSourceFileName_ModelIdentity", "ModelIdentity", ex); throw; }
				var modelIdentity = identityProp?.GetValue(serviceModelSessionToken);
				
				var identityType = FindTypeAnyOrThrow(assemblies, "Autodesk.RevitServer.Enterprise.Common.ClientServer.DataContract.Model.ModelIdentity");
				PropertyInfo guidProp = null;
				try { guidProp = identityType.GetProperty("IdentityGUID", BindingFlags.Public | BindingFlags.Instance); } catch (AmbiguousMatchException ex) { LogAmbiguity("GetSourceFileName_IdentityGUID", "IdentityGUID", ex); throw; }
				var guidValue = guidProp?.GetValue(modelIdentity);
				
				var guidValueType = FindTypeAnyOrThrow(assemblies, "Autodesk.RevitServer.Enterprise.Common.ClientServer.DataContract.Model.GUIDValue");
				PropertyInfo guidInnerProp = null;
				try { guidInnerProp = guidValueType.GetProperty("GUID", BindingFlags.Public | BindingFlags.Instance); } catch (AmbiguousMatchException ex) { LogAmbiguity("GetSourceFileName_GUID", "GUID", ex); throw; }
				var guid = (Guid)guidInnerProp.GetValue(guidValue);
				var combined = Path.Combine(guid.ToString(), fileName);
				return combined;
			}
			catch (AmbiguousMatchException ex)
			{
				// Debug: Log which property is causing ambiguity
				LogAmbiguity("GetSourceFileName", "unknown", ex);
				throw new InvalidOperationException($"GetSourceFileName ambiguity: {ex.Message}", ex);
			}
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
			PropertyInfo modelPathUtilsProp = null;
			try { modelPathUtilsProp = sharedUtilsType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static); } catch (AmbiguousMatchException ex) { LogAmbiguity("GenerateRvtFromModelData_Instance", "Instance", ex); throw; }
			var sharedUtilsInstance = modelPathUtilsProp?.GetValue(null);
			PropertyInfo innerPathUtilsProp = null;
			try { innerPathUtilsProp = sharedUtilsType.GetProperty("ModelPathUtils", BindingFlags.Public | BindingFlags.Instance); } catch (AmbiguousMatchException ex) { LogAmbiguity("GenerateRvtFromModelData_ModelPathUtils", "ModelPathUtils", ex); throw; }

			var versionMgr = Activator.CreateInstance(versionMgrType, new object[] { modelDataDir, latest });
			PropertyInfo setModelPathUtils = null;
			try { setModelPathUtils = iVersionMgrType.GetProperty("ModelPathUtils", BindingFlags.Public | BindingFlags.Instance); } catch (AmbiguousMatchException ex) { LogAmbiguity("GenerateRvtFromModelData_SetModelPathUtils", "ModelPathUtils", ex); throw; }
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

			// Get the specific overload that takes 3 IDictionary parameters (without Boolean)
			var getLatestStreamFiles = iVersionMgrType.GetMethod("GetLatestStreamFiles", 
				BindingFlags.Public | BindingFlags.Instance, 
				null, 
				new[] { dictStringString.MakeByRefType(), dictIntString.MakeByRefType(), dictIntString.MakeByRefType() }, 
				null);
			if (getLatestStreamFiles == null) throw new MissingMethodException(iVersionMgrType.FullName, "GetLatestStreamFiles(IDictionary<String,String>&, IDictionary<Int32,String>&, IDictionary<Int32,String>&)");
			var args = new object[] { nonElem, elem, steel };
			var ok = (bool)getLatestStreamFiles.Invoke(versionMgr, args);
			if (!ok) throw new InvalidOperationException("GetLatestStreamFiles returned false");

			// Use the correct RvtFile type name (ModelStorage.RvtFile, not OleFile.RvtFile)
			var rvtFileType = assemblies.GetType("Autodesk.RevitServer.Enterprise.Common.ClientServer.Helper.ModelStorage.RvtFile");
			if (rvtFileType == null) throw new TypeLoadException("RvtFile type not found: Autodesk.RevitServer.Enterprise.Common.ClientServer.Helper.ModelStorage.RvtFile");
			MethodInfo gen = null;
			try { gen = rvtFileType.GetMethod("GenerateRvtFileFromModelFolder", BindingFlags.Public | BindingFlags.Static); } catch (AmbiguousMatchException ex) { LogAmbiguity("GenerateRvtFromModelData_GenerateRvtFileFromModelFolder", "GenerateRvtFileFromModelFolder", ex); throw; }
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
			var result = p.Replace('|', Path.DirectorySeparatorChar);
			
			// DEBUG: Log the conversion to check encoding
			try
			{
				var logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RevitServerNet_Path_Conversion.txt");
				System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Path conversion:\n");
				System.IO.File.AppendAllText(logPath, $"  Input pipePath='{pipePath}'\n");
				System.IO.File.AppendAllText(logPath, $"  Output result='{result}'\n");
				System.IO.File.AppendAllText(logPath, $"  Input bytes: {string.Join(",", System.Text.Encoding.UTF8.GetBytes(pipePath))}\n");
				System.IO.File.AppendAllText(logPath, $"  Output bytes: {string.Join(",", System.Text.Encoding.UTF8.GetBytes(result))}\n\n");
			}
			catch { }
			
			return result;
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
			try
			{
				// Try Autodesk.* namespace first (older samples) then RS.* (packaged/in newer distros)
				Type t = null;
				try 
				{ 
					t = assemblies.GetType(autodeskFullName); 
					LogDebug("FindTypeAnyOrThrow", $"Successfully got type for '{autodeskFullName}': {t?.FullName}");
				} 
				catch (AmbiguousMatchException ex) 
				{ 
					LogAmbiguity("FindTypeAnyOrThrow", autodeskFullName, ex); 
					throw; 
				}
				if (t != null) return t;
				
				var rsName = autodeskFullName.Replace("Autodesk.RevitServer.", "RS.");
				try 
				{ 
					t = assemblies.GetType(rsName); 
					LogDebug("FindTypeAnyOrThrow", $"Successfully got type for '{rsName}': {t?.FullName}");
				} 
				catch (AmbiguousMatchException ex) 
				{ 
					LogAmbiguity("FindTypeAnyOrThrow", rsName, ex); 
					throw; 
				}
				if (t != null) return t;
				
				var rsEnterpriseName = autodeskFullName.Replace("Autodesk.RevitServer.Enterprise.Common.ClientServer", "RS.Enterprise.Common.ClientServer");
				try 
				{ 
					t = assemblies.GetType(rsEnterpriseName); 
					LogDebug("FindTypeAnyOrThrow", $"Successfully got type for '{rsEnterpriseName}': {t?.FullName}");
				} 
				catch (AmbiguousMatchException ex) 
				{ 
					LogAmbiguity("FindTypeAnyOrThrow", rsEnterpriseName, ex); 
					throw; 
				}
				if (t != null) return t;
				
				throw new TypeLoadException($"Type not found: {autodeskFullName}");
			}
			catch (AmbiguousMatchException ex)
			{
				// Debug: Log which type is causing ambiguity
				LogAmbiguity("FindTypeAnyOrThrow", autodeskFullName, ex);
				throw new InvalidOperationException($"FindTypeAnyOrThrow ambiguity for '{autodeskFullName}': {ex.Message}", ex);
			}
		}

		private static void LogDebug(string method, string message)
		{
			try
			{
				var logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"RevitServerNet_{method}_Debug.txt");
				System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
			}
			catch { }
		}

		private static void LogAmbiguity(string method, string typeName, AmbiguousMatchException ex)
		{
			try
			{
				var logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"RevitServerNet_{method}_Ambiguity.txt");
				System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {method} ambiguity for '{typeName}': {ex.Message}\n");
				System.IO.File.AppendAllText(logPath, $"Stack trace: {ex.StackTrace}\n");
			}
			catch { }
		}

		// Altec-like proxy usage helpers (moved inside class)
		private static object IdentifyModelExact(RsAssemblies assemblies, Type iModelServiceType, string revitVersion, string serverHost, object serviceSessionToken, string modelPipePath)
		{
			var proxyProviderType = FindTypeAnyOrThrow(assemblies, "Autodesk.RevitServer.Enterprise.Common.ClientServer.Proxy.ProxyProvider");
			var create = proxyProviderType.GetMethod("CreateProxyInstance", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null)
						 ?? proxyProviderType.GetMethod("CreateInstance", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
			if (create == null) throw new MissingMethodException(proxyProviderType.FullName, "CreateProxyInstance/CreateInstance");
			var provider = create.Invoke(null, new object[] { revitVersion });
			var getStreamed = FindGenericProxyMethodAny(provider.GetType(), new[] { "GetStreamedProxy" }, iModelServiceType);
			var clientProxy = getStreamed.Invoke(provider, new object[] { serverHost });
			var proxy = GetProxyFromClientProxy(clientProxy);
			var identifyMethod = iModelServiceType.GetMethod("IdentifyModel", BindingFlags.Public | BindingFlags.Instance);
			if (identifyMethod == null) throw new MissingMethodException(iModelServiceType.FullName, "IdentifyModel");
			var windowsPath = ConvertPipePathToRelativeWindowsPath(modelPipePath);
			return identifyMethod.Invoke(proxy, new object[] { serviceSessionToken, windowsPath, true });
		}

		private static object LockDataExact(RsAssemblies assemblies, Type iModelServiceType, string revitVersion, string serverHost, object serviceModelSessionToken)
		{
			var proxyProviderType = FindTypeAnyOrThrow(assemblies, "Autodesk.RevitServer.Enterprise.Common.ClientServer.Proxy.ProxyProvider");
			var create = proxyProviderType.GetMethod("CreateProxyInstance", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null)
						 ?? proxyProviderType.GetMethod("CreateInstance", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
			var provider = create.Invoke(null, new object[] { revitVersion });
			var getStreamed = FindGenericProxyMethodAny(provider.GetType(), new[] { "GetStreamedProxy" }, iModelServiceType);
			var clientProxy = getStreamed.Invoke(provider, new object[] { serverHost });
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

			var args = new object[] { serviceModelSessionToken, (uint)129, true, modelVersion, null };
			lockData.Invoke(proxy, args);
			return args[4];
		}

		private static IEnumerable<string> GetModelDataFileListExact(RsAssemblies assemblies, Type iModelServiceType, string revitVersion, string serverHost, object serviceModelSessionToken)
		{
			var proxyProviderType = FindTypeAnyOrThrow(assemblies, "Autodesk.RevitServer.Enterprise.Common.ClientServer.Proxy.ProxyProvider");
			var create = proxyProviderType.GetMethod("CreateProxyInstance", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null)
						 ?? proxyProviderType.GetMethod("CreateInstance", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
			var provider = create.Invoke(null, new object[] { revitVersion });
			var getStreamed = FindGenericProxyMethodAny(provider.GetType(), new[] { "GetStreamedProxy" }, iModelServiceType);
			var clientProxy = getStreamed.Invoke(provider, new object[] { serverHost });
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

		private static async Task DownloadAllFilesAsyncExact(
			RsAssemblies assemblies,
			Type iModelServiceType,
			string revitVersion,
			string serverHost,
			object serviceModelSessionToken,
			object creationDate,
			IEnumerable<string> fileList,
			string tempDir,
			IProgress<long> progress)
		{
			var proxyProviderType = FindTypeAnyOrThrow(assemblies, "Autodesk.RevitServer.Enterprise.Common.ClientServer.Proxy.ProxyProvider");
			var create = proxyProviderType.GetMethod("CreateProxyInstance", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null)
						 ?? proxyProviderType.GetMethod("CreateInstance", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
			var provider = create.Invoke(null, new object[] { revitVersion });
			var getStreamed = FindGenericProxyMethodAny(provider.GetType(), new[] { "GetStreamedProxy" }, iModelServiceType);
			var clientProxy = getStreamed.Invoke(provider, new object[] { serverHost });
			var proxy = GetProxyFromClientProxy(clientProxy);

			var requestType = FindTypeAnyOrThrow(assemblies, "Autodesk.RevitServer.Enterprise.Common.ClientServer.DataContract.Message.FileDownloadRequestMessage");
			// We'll extract the stream reflectively; no strict type dependency
			var downloadMethod = iModelServiceType.GetMethod("DownloadFile", BindingFlags.Public | BindingFlags.Instance, null, new[] { requestType }, null);
			if (downloadMethod == null) throw new MissingMethodException(iModelServiceType.FullName, "DownloadFile");
			var requestCtor = requestType.GetConstructor(new[] { FindTypeAnyOrThrow(assemblies, "Autodesk.RevitServer.Enterprise.Common.ClientServer.DataContract.SessionToken.ServiceModelSessionToken"), FindTypeAnyOrThrow(assemblies, "Autodesk.RevitServer.Enterprise.Common.ClientServer.DataContract.Model.EpisodeGuid"), typeof(string) });
			if (requestCtor == null) throw new MissingMethodException(requestType.FullName, ".ctor(ServiceModelSessionToken,EpisodeGuid,string)");

			foreach (var file in fileList)
			{
				var tokenForFile = CreateServiceModelSessionTokenFromExisting(assemblies, serviceModelSessionToken);
				var sourceFileName = GetSourceFileName(assemblies, serviceModelSessionToken, file);
				var request = requestCtor.Invoke(new[] { tokenForFile, creationDate, sourceFileName });
				var msg = downloadMethod.Invoke(proxy, new[] { request });
				if (msg == null) throw new InvalidOperationException("DownloadFile returned null message");
				using (var stream = ExtractStreamFromMessage(msg))
				{
					if (stream == null)
					{
						try
						{
							var dbg = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RevitServerNet_DownloadFile_MessageType.txt");
							var t = msg.GetType();
							using (var sw = new System.IO.StreamWriter(dbg, true, System.Text.Encoding.UTF8))
							{
								sw.WriteLine($"\n==== {DateTime.Now:yyyy-MM-dd HH:mm:ss} Message type dump ====");
								sw.WriteLine($"Type: {t.FullName}");
								try { sw.WriteLine($"Assembly: {t.Assembly.Location}"); } catch { }
								sw.WriteLine("Properties:");
								foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
								{
									sw.WriteLine($"  - {p.Name} : {p.PropertyType.FullName}");
								}
								sw.WriteLine("Fields:");
								foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
								{
									sw.WriteLine($"  - {f.Name} : {f.FieldType.FullName}");
								}
							}
						}
						catch { }
						throw new InvalidOperationException("DownloadFile returned null stream");
					}
					var target = Path.Combine(tempDir, Path.GetFileName(file));
					Directory.CreateDirectory(Path.GetDirectoryName(target));
					await CopyToFileAsync(stream, target, progress);
				}
			}
		}

		private static Stream ExtractStreamFromMessage(object message)
		{
			if (message == null) return null;
			var type = message.GetType();
			// Try property named "Stream"
			var prop = type.GetProperty("Stream", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
			if (prop != null && typeof(Stream).IsAssignableFrom(prop.PropertyType))
				return prop.GetValue(message) as Stream;
			// Try getter method patterns
			var getter = type.GetMethod("get_Stream", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) ?? type.GetMethod("GetStream", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
			if (getter != null && typeof(Stream).IsAssignableFrom(getter.ReturnType))
				return getter.Invoke(message, null) as Stream;
			// Fallback: search any readable Stream-typed property
			var anyProp = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).FirstOrDefault(p => typeof(Stream).IsAssignableFrom(p.PropertyType));
			if (anyProp != null) return anyProp.GetValue(message) as Stream;
			// Search fields
			var field = type.GetField("_stream", BindingFlags.NonPublic | BindingFlags.Instance) ?? type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).FirstOrDefault(f => typeof(Stream).IsAssignableFrom(f.FieldType));
			if (field != null) return field.GetValue(message) as Stream;
			try
			{
				var logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RevitServerNet_DownloadFile_MessageType.txt");
				using (var sw = new System.IO.StreamWriter(logPath, true, System.Text.Encoding.UTF8))
				{
					sw.WriteLine($"\n==== {DateTime.Now:yyyy-MM-dd HH:mm:ss} Message type dump ====");
					sw.WriteLine($"Type: {type.FullName}");
					try { sw.WriteLine($"Assembly: {type.Assembly.Location}"); } catch { }
					sw.WriteLine("Properties:");
					foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
					{
						sw.WriteLine($"  - {p.Name} : {p.PropertyType.FullName}");
					}
					sw.WriteLine("Fields:");
					foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
					{
						sw.WriteLine($"  - {f.Name} : {f.FieldType.FullName}");
					}
				}
			}
			catch { }
			return null;
		}

		private static Type ResolveTypeAny(RsAssemblies assemblies, string[] candidates)
		{
			foreach (var full in candidates)
			{
				var t = assemblies.GetType(full);
				if (t != null) return t;
			}
			return null;
		}

		private static Object GetGenericClientProxy(RsAssemblies assemblies, String revitVersion, String host, Boolean useStreamed)
		{
			try
			{
				var proxyProviderType = assemblies.GetType("Autodesk.RevitServer.Enterprise.Common.ClientServer.Proxy.ProxyProvider");
				if (proxyProviderType == null) throw new TypeLoadException("ProxyProvider type not found");
				
				// The patched DLL uses get_Instance instead of CreateProxyInstance
				object proxyProvider;
				var getInstanceMethod = proxyProviderType.GetMethod("get_Instance", BindingFlags.Public | BindingFlags.Static, null, new Type[0], null);
				if (getInstanceMethod != null)
				{
					// Use get_Instance (patched DLL)
					proxyProvider = getInstanceMethod.Invoke(null, new Object[0]);
				}
				else
				{
					// Fallback to original methods
					var createInstanceMethod = proxyProviderType.GetMethod("CreateProxyInstance", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(String) }, null);
					if (createInstanceMethod == null)
					{
						createInstanceMethod = proxyProviderType.GetMethod("CreateInstance", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(String) }, null);
						if (createInstanceMethod == null)
						{
							// Debug: List all available methods
							var methods = proxyProviderType.GetMethods(BindingFlags.Public | BindingFlags.Static);
							var methodNames = string.Join(", ", methods.Select(m => m.Name));
							throw new MissingMethodException($"No suitable factory method found. Available methods: {methodNames}");
						}
					}
					proxyProvider = createInstanceMethod.Invoke(null, new Object[] { revitVersion });
				}
				
				if (proxyProvider == null) throw new InvalidOperationException("ProxyProvider factory returned null");

				var iModelServiceType = assemblies.GetType("Autodesk.RevitServer.Enterprise.Common.ClientServer.ServiceContract.Model.IModelService");
				if (iModelServiceType == null) throw new TypeLoadException("IModelService type not found");
				
				var methodName = useStreamed ? "GetStreamedProxy" : "GetBufferedProxy";
				var genericMethod = proxyProviderType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(String) }, null);
				if (genericMethod == null) throw new MissingMethodException($"{methodName} method not found");
				
				var typedMethod = genericMethod.MakeGenericMethod(iModelServiceType);
				return typedMethod.Invoke(proxyProvider, new Object[] { host });
			}
			catch (AmbiguousMatchException ex)
			{
				// Debug: Log which method is causing ambiguity
				var logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RevitServerNet_GetGenericClientProxy_Ambiguity.txt");
				System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] GetGenericClientProxy ambiguity: {ex.Message}\n");
				System.IO.File.AppendAllText(logPath, $"Stack trace: {ex.StackTrace}\n");
				throw new InvalidOperationException($"GetGenericClientProxy ambiguity: {ex.Message}", ex);
			}
		}
	}
}

