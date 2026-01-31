using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Xml;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.ServiceModel.Description;

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
			catch (TargetInvocationException tie) when (TryGetFaultException(tie.InnerException, out var faultEx))
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

		private static bool TryGetFaultException(Exception ex, out Exception faultException)
		{
			faultException = null;
			if (ex == null) return false;
			var type = ex.GetType();
			if (type.FullName == "System.ServiceModel.FaultException")
			{
				faultException = ex;
				return true;
			}
			if (type.IsGenericType && type.GetGenericTypeDefinition().FullName == "System.ServiceModel.FaultException`1")
			{
				faultException = ex;
				return true;
			}
			if (type.Name.StartsWith("FaultException", StringComparison.Ordinal))
			{
				faultException = ex;
				return true;
			}
			return false;
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
			object clientProxy;
			try
			{
				clientProxy = GetGenericClientProxy(assemblies, revitVersion, serverHost, useStreamed: true);
			}
			catch (Exception ex)
			{
				// Fallback to buffered proxy when streaming binding is unavailable (e.g. net.tcp stream issues)
				try
				{
					var logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RevitServerNet_StreamProxy_Fallback.txt");
					System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Streamed proxy failed: {ex.GetBaseException().Message}\n");
				}
				catch { }
				clientProxy = GetGenericClientProxy(assemblies, revitVersion, serverHost, useStreamed: false);
			}
			var iModelServiceType = assemblies.GetType("Autodesk.RevitServer.Enterprise.Common.ClientServer.ServiceContract.Model.IModelService");
			if (iModelServiceType == null) throw new TypeLoadException("IModelService type not found");
			
			var proxy = GetProxyFromClientProxy(clientProxy);
			TryConfigureClientProxyBinding(proxy);
			
			// If channel is already open, we need to recreate with proper binding
			proxy = TryRecreateProxyWithLargeMessageSupport(clientProxy, proxy, iModelServiceType, serverHost);
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


		private static void TryCleanupTemp(string dir)
		{
			try { if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
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

		private static Stream ExtractStreamFromMessage(object message)
		{
			if (message == null) return null;
			if (message is byte[] directBytes)
				return new MemoryStream(directBytes, writable: false);
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
			// Try byte[] payloads (buffered responses)
			var byteProp = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
				.FirstOrDefault(p => p.PropertyType == typeof(byte[]) &&
									 (p.Name.Equals("Data", StringComparison.OrdinalIgnoreCase) ||
									  p.Name.Equals("Bytes", StringComparison.OrdinalIgnoreCase) ||
									  p.Name.Equals("Buffer", StringComparison.OrdinalIgnoreCase) ||
									  p.Name.Equals("Content", StringComparison.OrdinalIgnoreCase)));
			if (byteProp != null)
			{
				var bytes = byteProp.GetValue(message) as byte[];
				if (bytes != null) return new MemoryStream(bytes, writable: false);
			}
			var byteField = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
				.FirstOrDefault(f => f.FieldType == typeof(byte[]));
			if (byteField != null)
			{
				var bytes = byteField.GetValue(message) as byte[];
				if (bytes != null) return new MemoryStream(bytes, writable: false);
			}
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
				var clientProxy = typedMethod.Invoke(proxyProvider, new Object[] { host });
				TryConfigureClientProxyBinding(clientProxy);
				// Some proxy providers expose the actual channel via Proxy property
				try
				{
					var innerProxy = GetProxyFromClientProxy(clientProxy);
					TryConfigureClientProxyBinding(innerProxy);
				}
				catch
				{
					// Ignore: not all proxies expose Proxy property at this stage
				}
				return clientProxy;
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

		private const long MaxMessageSizeBytes = 5L * 1024L * 1024L * 1024L; // 5 GB
		private const int MaxBufferSizeBytes = int.MaxValue; // max supported buffer size

		private static void TryConfigureClientProxyBinding(object clientProxy)
		{
			if (clientProxy == null) return;
			try
			{
				var binding = TryGetBindingFromClientProxy(clientProxy);
				LogBindingDiagnostics("TryConfigureClientProxyBinding_Before", clientProxy, binding);
				if (binding == null) return;
				ConfigureBindingLimits(binding);
				LogBindingDiagnostics("TryConfigureClientProxyBinding_After", clientProxy, binding);
			}
			catch (Exception ex)
			{
				// Log binding configuration failures for debugging
				try
				{
					var logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RevitServerNet_BindingConfig_Error.txt");
					System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] TryConfigureClientProxyBinding error: {ex.Message}\n{ex.StackTrace}\n\n");
				}
				catch { }
			}
		}

		private static void LogBindingDiagnostics(string context, object clientProxy, Binding binding)
		{
			try
			{
				var logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RevitServerNet_Binding_Diagnostics.txt");
				var sb = new System.Text.StringBuilder();
				sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] === {context} ===");
				sb.AppendLine($"ClientProxy type: {clientProxy?.GetType().FullName ?? "null"}");
				sb.AppendLine($"Binding type: {binding?.GetType().FullName ?? "null"}");
				
				if (binding != null)
				{
					sb.AppendLine($"Binding.Name: {binding.Name}");
					sb.AppendLine($"Binding.Scheme: {binding.Scheme}");
					
					if (binding is NetTcpBinding netTcp)
					{
						sb.AppendLine($"NetTcpBinding.MaxReceivedMessageSize: {netTcp.MaxReceivedMessageSize}");
						sb.AppendLine($"NetTcpBinding.MaxBufferSize: {netTcp.MaxBufferSize}");
						sb.AppendLine($"NetTcpBinding.TransferMode: {netTcp.TransferMode}");
					}
					else if (binding is CustomBinding custom)
					{
						sb.AppendLine($"CustomBinding elements ({custom.Elements.Count}):");
						foreach (var elem in custom.Elements)
						{
							sb.AppendLine($"  - {elem.GetType().Name}");
							if (elem is TransportBindingElement transport)
							{
								sb.AppendLine($"    MaxReceivedMessageSize: {transport.MaxReceivedMessageSize}");
							}
						}
					}
				}

				// Check if channel is already open
				if (clientProxy is ICommunicationObject commObj)
				{
					sb.AppendLine($"CommunicationState: {commObj.State}");
				}
				
				sb.AppendLine();
				System.IO.File.AppendAllText(logPath, sb.ToString());
			}
			catch { }
		}

		private static object TryRecreateProxyWithLargeMessageSupport(object clientProxy, object innerProxy, Type iModelServiceType, string serverHost)
		{
			try
			{
				// Check if the channel is already opened - if so, binding changes won't take effect
				if (!(innerProxy is ICommunicationObject commObj) || commObj.State != CommunicationState.Opened)
				{
					LogBindingError("TryRecreateProxy", $"Channel not opened yet (State={((innerProxy as ICommunicationObject)?.State.ToString() ?? "not ICommunicationObject")}), binding config should work");
					return innerProxy; // Not opened yet, binding config should work
				}

				LogBindingError("TryRecreateProxy", $"Channel is OPENED, attempting to recreate with large message binding");

				// Get the endpoint info from existing proxy - try multiple sources
				LogBindingError("TryRecreateProxy", "Attempting to get endpoint address...");
				var endpointAddress = TryGetEndpointAddress(clientProxy);
				if (endpointAddress != null)
				{
					LogBindingError("TryRecreateProxy", $"Got endpoint from clientProxy: {endpointAddress.Uri}");
				}
				else
				{
					endpointAddress = TryGetEndpointAddress(innerProxy);
					if (endpointAddress != null)
					{
						LogBindingError("TryRecreateProxy", $"Got endpoint from innerProxy: {endpointAddress.Uri}");
					}
				}
				
				if (endpointAddress == null)
				{
					// Cannot create proxy without knowing the correct endpoint - return original
					LogBindingError("TryRecreateProxy", "FAILED: Cannot determine endpoint address, returning original proxy");
					return innerProxy;
				}

				// Use the correct IModelService type from Autodesk assemblies
				if (iModelServiceType == null)
				{
					LogBindingError("TryRecreateProxy", "IModelService type is null, cannot recreate proxy");
					return innerProxy;
				}
				LogBindingError("TryRecreateProxy", $"Using contract type: {iModelServiceType.FullName} from {iModelServiceType.Assembly.GetName().Name}");

				// Create new binding with large message support (pass URI to determine TransferMode)
				var newBinding = CreateLargeMessageBinding(endpointAddress.Uri.Scheme, endpointAddress.Uri);
				if (newBinding == null)
				{
					LogBindingError("TryRecreateProxy", $"Cannot create binding for scheme: {endpointAddress.Uri.Scheme}");
					return innerProxy;
				}
				var netTcpBinding = newBinding as NetTcpBinding;
				LogBindingError("TryRecreateProxy", $"Created {newBinding.GetType().Name} with MaxReceivedMessageSize={netTcpBinding?.MaxReceivedMessageSize}, TransferMode={netTcpBinding?.TransferMode}");

				// DON'T close the old channel yet - only close after new one works!

				// Create new channel factory and channel using reflection (with Autodesk's IModelService type)
				var channelFactoryType = typeof(ChannelFactory<>).MakeGenericType(iModelServiceType);
				object factory;
				try
				{
					factory = Activator.CreateInstance(channelFactoryType, newBinding, endpointAddress);
					LogBindingError("TryRecreateProxy", $"Created ChannelFactory<{iModelServiceType.Name}>");
				}
				catch (Exception factoryEx)
				{
					var inner = factoryEx.InnerException?.Message ?? factoryEx.Message;
					LogBindingError("TryRecreateProxy", $"Failed to create ChannelFactory: {inner}");
					return innerProxy; // Return original (still open)
				}
				
				// DON'T call ConfigureBindingLimits here - the binding was already created with correct settings
				// Calling it would overwrite MaxReceivedMessageSize with 5GB (long) which breaks Buffered mode

				// Create channel
				object newProxy;
				var createChannelMethod = channelFactoryType.GetMethod("CreateChannel", Type.EmptyTypes);
				try
				{
					newProxy = createChannelMethod?.Invoke(factory, null);
				}
				catch (Exception createEx)
				{
					var inner = createEx.InnerException?.Message ?? createEx.Message;
					var innerInner = createEx.InnerException?.InnerException?.Message;
					LogBindingError("TryRecreateProxy", $"CreateChannel failed: {inner}" + (innerInner != null ? $" -> {innerInner}" : ""));
					return innerProxy; // Return original (still open)
				}
				
				if (newProxy != null)
				{
					// Open the channel before use
					if (newProxy is ICommunicationObject newChannel)
					{
						try
						{
							newChannel.Open();
							LogBindingError("TryRecreateProxy", $"New channel opened successfully, State={newChannel.State}");
						}
						catch (Exception openEx)
						{
							var inner = openEx.InnerException?.Message ?? openEx.Message;
							LogBindingError("TryRecreateProxy", $"Failed to open new channel: {inner}");
							return innerProxy; // Return original (still open)
						}
					}
					
					// NOW close the old channel since new one works
					try 
					{ 
						commObj.Close(TimeSpan.FromSeconds(5)); 
						LogBindingError("TryRecreateProxy", "Old channel closed successfully");
					} 
					catch
					{ 
						try { commObj.Abort(); } catch { } 
					}
					
					LogBindingError("TryRecreateProxy", $"SUCCESS: Created and opened new channel with large message binding for {iModelServiceType.Name}");
					return newProxy;
				}
				else
				{
					LogBindingError("TryRecreateProxy", "CreateChannel returned null");
				}
			}
			catch (Exception ex)
			{
				var inner = ex.InnerException?.Message ?? "";
				var innerInner = ex.InnerException?.InnerException?.Message ?? "";
				LogBindingError("TryRecreateProxy", $"Failed to recreate proxy: {ex.GetType().Name}: {ex.Message}\nInner: {inner}\nInnerInner: {innerInner}\n{ex.StackTrace}");
			}
			return innerProxy;
		}

		private static EndpointAddress TryGetEndpointAddress(object proxy)
		{
			if (proxy == null) return null;
			try
			{
				var proxyType = proxy.GetType();
				LogBindingError("TryGetEndpointAddress", $"Trying to get endpoint from {proxyType.FullName}");
				
				// Try IClientChannel.RemoteAddress first (most reliable for WCF proxies)
				if (proxy is IClientChannel clientChannel)
				{
					LogBindingError("TryGetEndpointAddress", $"Proxy is IClientChannel, RemoteAddress={clientChannel.RemoteAddress?.Uri}");
					if (clientChannel.RemoteAddress != null) return clientChannel.RemoteAddress;
				}
				
				// Try Endpoint.Address
				var endpointProp = proxyType.GetProperty("Endpoint", BindingFlags.Public | BindingFlags.Instance);
				if (endpointProp != null)
				{
					var endpoint = endpointProp.GetValue(proxy) as ServiceEndpoint;
					LogBindingError("TryGetEndpointAddress", $"Endpoint property found, Address={endpoint?.Address?.Uri}");
					if (endpoint?.Address != null) return endpoint.Address;
				}

				// Try RemoteAddress property directly
				var remoteAddrProp = proxyType.GetProperty("RemoteAddress", BindingFlags.Public | BindingFlags.Instance);
				if (remoteAddrProp != null)
				{
					var addr = remoteAddrProp.GetValue(proxy) as EndpointAddress;
					LogBindingError("TryGetEndpointAddress", $"RemoteAddress property found, Uri={addr?.Uri}");
					if (addr != null) return addr;
				}

				// Try ChannelFactory.Endpoint.Address
				var cfProp = proxyType.GetProperty("ChannelFactory", BindingFlags.Public | BindingFlags.Instance);
				if (cfProp != null)
				{
					var cf = cfProp.GetValue(proxy);
					if (cf != null)
					{
						var cfEndpointProp = cf.GetType().GetProperty("Endpoint", BindingFlags.Public | BindingFlags.Instance);
						var cfEndpoint = cfEndpointProp?.GetValue(cf) as ServiceEndpoint;
						LogBindingError("TryGetEndpointAddress", $"ChannelFactory.Endpoint.Address={cfEndpoint?.Address?.Uri}");
						if (cfEndpoint?.Address != null) return cfEndpoint.Address;
					}
				}
				
				// Try to find any property that returns EndpointAddress
				foreach (var prop in proxyType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
				{
					if (typeof(EndpointAddress).IsAssignableFrom(prop.PropertyType))
					{
						try
						{
							var addr = prop.GetValue(proxy) as EndpointAddress;
							if (addr != null)
							{
								LogBindingError("TryGetEndpointAddress", $"Found EndpointAddress in property {prop.Name}: {addr.Uri}");
								return addr;
							}
						}
						catch { }
					}
				}
				
				// Try to find Via property (sometimes used in WCF)
				var viaProp = proxyType.GetProperty("Via", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
				if (viaProp != null)
				{
					var via = viaProp.GetValue(proxy) as Uri;
					if (via != null)
					{
						LogBindingError("TryGetEndpointAddress", $"Found Via property: {via}");
						return new EndpointAddress(via);
					}
				}
				
				LogBindingError("TryGetEndpointAddress", "No endpoint address found via standard properties");
				
				// Last resort: dump all properties and fields to find URI
				DumpProxyStructure(proxy, proxyType);
				
				// Try to find URI in any field
				foreach (var field in proxyType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
				{
					try
					{
						var val = field.GetValue(proxy);
						if (val is Uri uri)
						{
							LogBindingError("TryGetEndpointAddress", $"Found Uri in field {field.Name}: {uri}");
							return new EndpointAddress(uri);
						}
						if (val is EndpointAddress ea)
						{
							LogBindingError("TryGetEndpointAddress", $"Found EndpointAddress in field {field.Name}: {ea.Uri}");
							return ea;
						}
						// Check nested object for RemoteAddress
						if (val != null && val is ICommunicationObject)
						{
							var nestedAddr = TryGetEndpointAddressFromObject(val);
							if (nestedAddr != null) return nestedAddr;
						}
					}
					catch { }
				}
			}
			catch (Exception ex)
			{
				LogBindingError("TryGetEndpointAddress", $"Exception: {ex.Message}");
			}
			return null;
		}

		private static EndpointAddress TryGetEndpointAddressFromObject(object obj)
		{
			if (obj == null) return null;
			try
			{
				var t = obj.GetType();
				// Try RemoteAddress
				var raProp = t.GetProperty("RemoteAddress", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
				if (raProp != null)
				{
					var ra = raProp.GetValue(obj) as EndpointAddress;
					if (ra != null)
					{
						LogBindingError("TryGetEndpointAddressFromObject", $"Found RemoteAddress: {ra.Uri}");
						return ra;
					}
				}
				// Try Via
				var viaProp = t.GetProperty("Via", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
				if (viaProp != null)
				{
					var via = viaProp.GetValue(obj) as Uri;
					if (via != null)
					{
						LogBindingError("TryGetEndpointAddressFromObject", $"Found Via: {via}");
						return new EndpointAddress(via);
					}
				}
			}
			catch { }
			return null;
		}

		private static void DumpProxyStructure(object proxy, Type proxyType)
		{
			try
			{
				var sb = new System.Text.StringBuilder();
				sb.AppendLine($"=== Proxy Structure Dump for {proxyType.FullName} ===");
				
				sb.AppendLine("Interfaces:");
				foreach (var iface in proxyType.GetInterfaces())
				{
					sb.AppendLine($"  - {iface.FullName}");
				}
				
				sb.AppendLine("Properties:");
				foreach (var prop in proxyType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
				{
					try
					{
						var val = prop.GetValue(proxy);
						sb.AppendLine($"  - {prop.Name}: {prop.PropertyType.Name} = {val}");
					}
					catch (Exception ex)
					{
						sb.AppendLine($"  - {prop.Name}: {prop.PropertyType.Name} = [ERROR: {ex.Message}]");
					}
				}
				
				sb.AppendLine("Fields:");
				foreach (var field in proxyType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
				{
					try
					{
						var val = field.GetValue(proxy);
						sb.AppendLine($"  - {field.Name}: {field.FieldType.Name} = {val}");
					}
					catch (Exception ex)
					{
						sb.AppendLine($"  - {field.Name}: {field.FieldType.Name} = [ERROR: {ex.Message}]");
					}
				}
				
				LogBindingError("DumpProxyStructure", sb.ToString());
			}
			catch { }
		}

		private static Binding CreateLargeMessageBinding(string scheme, Uri endpointUri = null)
		{
			if (string.Equals(scheme, "net.tcp", StringComparison.OrdinalIgnoreCase))
			{
				// Determine TransferMode from endpoint path
				// /tcpbuffer -> Buffered, /tcpstreamed -> Streamed
				var transferMode = TransferMode.Buffered; // Default to buffered
				if (endpointUri != null)
				{
					var path = endpointUri.AbsolutePath.ToLowerInvariant();
					if (path.Contains("stream"))
					{
						transferMode = TransferMode.Streamed;
					}
					else if (path.Contains("buffer"))
					{
						transferMode = TransferMode.Buffered;
					}
					LogBindingError("CreateLargeMessageBinding", $"Endpoint path '{path}' -> TransferMode.{transferMode}");
				}
				
				// For Buffered mode: MaxReceivedMessageSize must fit in int (max ~2GB)
				// For Streamed mode: MaxReceivedMessageSize can be long (up to 5GB+)
				long maxMsgSize;
				int maxBufSize;
				if (transferMode == TransferMode.Buffered)
				{
					// Buffered: both must be int, and MaxBufferSize == MaxReceivedMessageSize
					maxMsgSize = int.MaxValue;
					maxBufSize = int.MaxValue;
				}
				else
				{
					// Streamed: MaxReceivedMessageSize can be large, MaxBufferSize is just for headers
					maxMsgSize = MaxMessageSizeBytes; // 5GB
					maxBufSize = 65536; // Small buffer for headers only
				}
				
				var binding = new NetTcpBinding(SecurityMode.None)
				{
					MaxReceivedMessageSize = maxMsgSize,
					MaxBufferSize = maxBufSize,
					MaxBufferPoolSize = maxBufSize,
					TransferMode = transferMode,
					OpenTimeout = TimeSpan.FromMinutes(10),
					CloseTimeout = TimeSpan.FromMinutes(10),
					SendTimeout = TimeSpan.FromMinutes(30),
					ReceiveTimeout = TimeSpan.FromMinutes(30)
				};
				ApplyReaderQuotas(binding.ReaderQuotas);
				LogBindingError("CreateLargeMessageBinding", $"Created binding: TransferMode={transferMode}, MaxReceivedMessageSize={maxMsgSize}, MaxBufferSize={maxBufSize}");
				return binding;
			}
			return null;
		}

		private static void LogBindingError(string method, string message)
		{
			try
			{
				var logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RevitServerNet_Binding_Diagnostics.txt");
				System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{method}] {message}\n");
			}
			catch { }
		}

		private static Binding TryGetBindingFromClientProxy(object clientProxy)
		{
			if (clientProxy == null) return null;
			var proxyType = clientProxy.GetType();

			// Try Endpoint property (ClientBase<T>)
			var endpointProp = proxyType.GetProperty("Endpoint", BindingFlags.Public | BindingFlags.Instance);
			if (endpointProp != null)
			{
				var endpoint = endpointProp.GetValue(clientProxy) as ServiceEndpoint;
				if (endpoint?.Binding != null) return endpoint.Binding;
			}

			// Try ChannelFactory property
			var channelFactoryProp = proxyType.GetProperty("ChannelFactory", BindingFlags.Public | BindingFlags.Instance);
			if (channelFactoryProp != null)
			{
				var channelFactory = channelFactoryProp.GetValue(clientProxy);
				var cfBinding = TryGetBindingFromChannelFactory(channelFactory);
				if (cfBinding != null) return cfBinding;
			}

			// Try Binding property directly
			var bindingProp = proxyType.GetProperty("Binding", BindingFlags.Public | BindingFlags.Instance);
			if (bindingProp != null)
			{
				return bindingProp.GetValue(clientProxy) as Binding;
			}

			// Try IClientChannel explicit implementations (dynamic WCF proxies)
			if (clientProxy is IClientChannel clientChannel)
			{
				var channelFactory = TryGetPropertyValue(clientChannel, "ChannelFactory");
				var cfBinding = TryGetBindingFromChannelFactory(channelFactory);
				if (cfBinding != null) return cfBinding;
			}

			return null;
		}

		private static Binding TryGetBindingFromChannelFactory(object channelFactory)
		{
			if (channelFactory == null) return null;
			var cfEndpointProp = channelFactory.GetType().GetProperty("Endpoint", BindingFlags.Public | BindingFlags.Instance);
			var cfEndpoint = cfEndpointProp?.GetValue(channelFactory) as ServiceEndpoint;
			return cfEndpoint?.Binding;
		}

		private static object TryGetPropertyValue(object target, string propertyName)
		{
			if (target == null || string.IsNullOrWhiteSpace(propertyName)) return null;
			try
			{
				var t = target.GetType();
				var prop = t.GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
				if (prop != null) return prop.GetValue(target);

				// Try explicit interface implementation names
				var explicitProp = t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
					.FirstOrDefault(p => p.Name.EndsWith("." + propertyName, StringComparison.Ordinal));
				return explicitProp?.GetValue(target);
			}
			catch
			{
				return null;
			}
		}

		private static void ConfigureBindingLimits(Binding binding)
		{
			if (binding == null) return;

			switch (binding)
			{
				case NetTcpBinding netTcp:
					netTcp.MaxReceivedMessageSize = MaxMessageSizeBytes;
					netTcp.MaxBufferSize = MaxBufferSizeBytes;
					netTcp.MaxBufferPoolSize = Math.Max(netTcp.MaxBufferPoolSize, MaxBufferSizeBytes);
					ApplyReaderQuotas(netTcp.ReaderQuotas);
					return;

				case CustomBinding custom:
					foreach (var element in custom.Elements)
					{
						if (element is TransportBindingElement transport)
						{
							transport.MaxReceivedMessageSize = MaxMessageSizeBytes;
						}
						if (element is TextMessageEncodingBindingElement textEncoding)
						{
							ApplyReaderQuotas(textEncoding.ReaderQuotas);
						}
						if (element is BinaryMessageEncodingBindingElement binaryEncoding)
						{
							ApplyReaderQuotas(binaryEncoding.ReaderQuotas);
						}
						if (element is MtomMessageEncodingBindingElement mtomEncoding)
						{
							ApplyReaderQuotas(mtomEncoding.ReaderQuotas);
						}
					}
					return;
			}
		}

		private static void ApplyReaderQuotas(XmlDictionaryReaderQuotas quotas)
		{
			if (quotas == null) return;
			quotas.MaxDepth = Math.Max(quotas.MaxDepth, 64);
			quotas.MaxStringContentLength = int.MaxValue;
			quotas.MaxArrayLength = int.MaxValue;
			quotas.MaxBytesPerRead = int.MaxValue;
			quotas.MaxNameTableCharCount = int.MaxValue;
		}
	}
}

