using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
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

			var (proxyProviderInstance, iModelServiceType) = CreateProxyProviderAndModelServiceType(assemblies, options.ServerHost, options.RevitVersion, out var getBufferedProxy, out var getStreamedProxy);

			var serviceSessionToken = CreateServiceSessionToken(assemblies);
			var modelIdentity = IdentifyModel(assemblies, proxyProviderInstance, iModelServiceType, getBufferedProxy, serviceSessionToken, options.ServerHost, options.ModelPipePath);
			var serviceModelSessionToken = CreateServiceModelSessionToken(assemblies, modelIdentity, options.ServerHost, options.ModelPipePath);

			var creationDate = LockData(assemblies, proxyProviderInstance, iModelServiceType, getBufferedProxy, serviceModelSessionToken, options.ServerHost);

			var tempDir = CreateTempModelDataFolder();
			try
			{
				var fileList = GetModelDataFileList(assemblies, proxyProviderInstance, iModelServiceType, getBufferedProxy, serviceModelSessionToken, options.ServerHost);
				await DownloadAllFilesAsync(assemblies, proxyProviderInstance, iModelServiceType, getStreamedProxy, serviceModelSessionToken, creationDate, fileList, tempDir, options.ServerHost, bytesProgress);

				GenerateRvtFromModelData(assemblies, tempDir, options.DestinationFile, options.Overwrite);
			}
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
			var proxyProviderType = FindTypeOrThrow(assemblies, "Autodesk.RevitServer.Enterprise.Common.ClientServer.Proxy.ProxyProvider");
			var createInstance = proxyProviderType.GetMethod("CreateProxyInstance", BindingFlags.Public | BindingFlags.Static);
			if (createInstance == null) throw new MissingMethodException(proxyProviderType.FullName, "CreateProxyInstance");
			var provider = createInstance.Invoke(null, new object[] { revitVersion });
			if (provider == null) throw new InvalidOperationException("ProxyProvider.CreateProxyInstance returned null");

			var iModelServiceType = FindTypeOrThrow(assemblies, "Autodesk.RevitServer.Enterprise.Common.ClientServer.ServiceContract.Model.IModelService");
			getBufferedProxyMethod = FindGenericProxyMethod(provider.GetType(), "GetBufferedProxy", iModelServiceType);
			getStreamedProxyMethod = FindGenericProxyMethod(provider.GetType(), "GetStreamedProxy", iModelServiceType);
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

		private static string CreateTempModelDataFolder()
		{
			var name = $"RevitServerNet_ModelData_{Guid.NewGuid():N}";
			var dir = Path.Combine(Path.GetTempPath(), name);
			Directory.CreateDirectory(dir);
			return dir;
		}

		private static object CreateServiceSessionToken(RsAssemblies assemblies)
		{
			var type = FindTypeOrThrow(assemblies, "Autodesk.RevitServer.Enterprise.Common.ClientServer.DataContract.SessionToken.ServiceSessionToken");
			var ctor = type.GetConstructor(new[] { typeof(string), typeof(string), typeof(string), typeof(string) });
			if (ctor == null) throw new MissingMethodException(type.FullName, ".ctor(string,string,string,string)");
			var userName = $"RevitServerNet:{Environment.MachineName}:1";
			var token = ctor.Invoke(new object[] { userName, string.Empty, Environment.MachineName, Guid.NewGuid().ToString() });
			return token;
		}

		private static object CreateServiceModelSessionToken(RsAssemblies assemblies, object modelIdentity, string serverHost, string modelPipePath)
		{
			var type = FindTypeOrThrow(assemblies, "Autodesk.RevitServer.Enterprise.Common.ClientServer.DataContract.SessionToken.ServiceModelSessionToken");
			var ctor = type.GetConstructor(new[] { FindTypeOrThrow(assemblies, "Autodesk.RevitServer.Enterprise.Common.ClientServer.DataContract.Model.ModelIdentity"), typeof(string), typeof(string), typeof(string), typeof(string) });
			if (ctor == null) throw new MissingMethodException(type.FullName, ".ctor(ModelIdentity,string,string,string,string)");
			var userName = $"RevitServerNet:{Environment.MachineName}:1";
			var token = ctor.Invoke(new object[] { modelIdentity, userName, string.Empty, Environment.MachineName, Guid.NewGuid().ToString() });
			// set ModelLocation property
			var modelLocation = CreateModelLocation(assemblies, serverHost, modelPipePath);
			var prop = type.GetProperty("ModelLocation", BindingFlags.Public | BindingFlags.Instance);
			if (prop != null && prop.CanWrite)
				prop.SetValue(token, modelLocation);
			return token;
		}

		private static object CreateModelLocation(RsAssemblies assemblies, string serverHost, string modelPipePath)
		{
			var type = FindTypeOrThrow(assemblies, "Autodesk.RevitServer.Enterprise.Common.ClientServer.DataContract.Model.ModelLocation");
			var enumType = FindTypeOrThrow(assemblies, "Autodesk.RevitServer.Enterprise.Common.ClientServer.DataContract.Model.ModelLocationType");
			var serverEnum = Enum.Parse(enumType, "Server");
			var ctor = type.GetConstructor(new[] { typeof(string), typeof(string), enumType });
			if (ctor == null) throw new MissingMethodException(type.FullName, ".ctor(string,string,ModelLocationType)");
			var relativePath = ConvertPipePathToRelativeWindowsPath(modelPipePath);
			return ctor.Invoke(new object[] { serverHost, relativePath, serverEnum });
		}

		private static object IdentifyModel(RsAssemblies assemblies, object proxyProvider, Type iModelServiceType, MethodInfo getBufferedProxy, object serviceSessionToken, string serverHost, string modelPipePath)
		{
			var clientProxy = getBufferedProxy.Invoke(proxyProvider, new object[] { serverHost });
			var proxy = GetProxyFromClientProxy(clientProxy);
			var identifyMethod = iModelServiceType.GetMethod("IdentifyModel", BindingFlags.Public | BindingFlags.Instance);
			if (identifyMethod == null) throw new MissingMethodException(iModelServiceType.FullName, "IdentifyModel");
			var path = ConvertPipePathToRelativeWindowsPath(modelPipePath);
			var result = identifyMethod.Invoke(proxy, new object[] { serviceSessionToken, path, true });
			return result;
		}

		private static object LockData(RsAssemblies assemblies, object proxyProvider, Type iModelServiceType, MethodInfo getBufferedProxy, object serviceModelSessionToken, string serverHost)
		{
			var clientProxy = getBufferedProxy.Invoke(proxyProvider, new object[] { serverHost });
			var proxy = GetProxyFromClientProxy(clientProxy);
			var lockData = iModelServiceType.GetMethod("LockData", BindingFlags.Public | BindingFlags.Instance);
			if (lockData == null) throw new MissingMethodException(iModelServiceType.FullName, "LockData");

			var modelVersionType = FindTypeOrThrow(assemblies, "Autodesk.RevitServer.Enterprise.Common.ClientServer.DataContract.Model.ModelVersion");
			var versionNumberType = FindTypeOrThrow(assemblies, "Autodesk.RevitServer.Enterprise.Common.ClientServer.DataContract.Model.VersionNumber");
			var historyCheckInfoType = FindTypeOrThrow(assemblies, "Autodesk.RevitServer.Enterprise.Common.ClientServer.DataContract.Model.ModelHistoryCheckInfo");
			var episodeGuidType = FindTypeOrThrow(assemblies, "Autodesk.RevitServer.Enterprise.Common.ClientServer.DataContract.Model.EpisodeGuid");

			var episodeInvalid = episodeGuidType.GetField("Invalid", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
			var historyCtor = historyCheckInfoType.GetConstructor(new[] { episodeGuidType });
			var history = historyCtor.Invoke(new[] { episodeInvalid });
			var versionNumberCtor = versionNumberType.GetConstructor(new[] { typeof(int) });
			var versionNumber = versionNumberCtor.Invoke(new object[] { 0 });
			var modelVersionCtor = modelVersionType.GetConstructor(new[] { versionNumberType, historyCheckInfoType });
			var modelVersion = modelVersionCtor.Invoke(new[] { versionNumber, history });

			// Prepare args with out parameter
			var args = new object[] { serviceModelSessionToken, (uint)129, true, modelVersion, null };
			lockData.Invoke(proxy, args);
			return args[4]; // creationDate (EpisodeGuid)
		}

		private static IEnumerable<string> GetModelDataFileList(RsAssemblies assemblies, object proxyProvider, Type iModelServiceType, MethodInfo getBufferedProxy, object serviceModelSessionToken, string serverHost)
		{
			var clientProxy = getBufferedProxy.Invoke(proxyProvider, new object[] { serverHost });
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

			var requestType = FindTypeOrThrow(assemblies, "Autodesk.RevitServer.Enterprise.Common.ClientServer.DataContract.Message.FileDownloadRequestMessage");
			var messageStreamType = FindTypeOrThrow(assemblies, "Autodesk.RevitServer.Enterprise.Common.ClientServer.DataContract.Message.FileDownloadMessageStream");
			var downloadMethod = iModelServiceType.GetMethod("DownloadFile", BindingFlags.Public | BindingFlags.Instance, null, new[] { requestType }, null);
			if (downloadMethod == null) throw new MissingMethodException(iModelServiceType.FullName, "DownloadFile");

			var requestCtor = requestType.GetConstructor(new[] { FindTypeOrThrow(assemblies, "Autodesk.RevitServer.Enterprise.Common.ClientServer.DataContract.SessionToken.ServiceModelSessionToken"), FindTypeOrThrow(assemblies, "Autodesk.RevitServer.Enterprise.Common.ClientServer.DataContract.Model.EpisodeGuid"), typeof(string) });
			if (requestCtor == null) throw new MissingMethodException(requestType.FullName, ".ctor(ServiceModelSessionToken,EpisodeGuid,string)");

			var streamProp = messageStreamType.GetProperty("Stream", BindingFlags.Public | BindingFlags.Instance);
			if (streamProp == null) throw new MissingMemberException(messageStreamType.FullName, "Stream");

			foreach (var file in fileList)
			{
				var request = requestCtor.Invoke(new[] { serviceModelSessionToken, creationDate, GetSourceFileName(assemblies, serviceModelSessionToken, file) });
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
			var identityType = FindTypeOrThrow(assemblies, "Autodesk.RevitServer.Enterprise.Common.ClientServer.DataContract.Model.ModelIdentity");
			var guidProp = identityType.GetProperty("IdentityGUID", BindingFlags.Public | BindingFlags.Instance);
			var guidValue = guidProp?.GetValue(modelIdentity);
			var guidValueType = FindTypeOrThrow(assemblies, "Autodesk.RevitServer.Enterprise.Common.ClientServer.DataContract.Model.GUIDValue");
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

			var dataFormatVersionType = FindTypeOrThrow(assemblies, "Autodesk.RevitServer.Enterprise.Common.ClientServer.DataContract.Model.DataFormatVersion");
			var latest = Enum.Parse(dataFormatVersionType, "Latest");

			var versionMgrType = FindTypeOrThrow(assemblies, "Autodesk.RevitServer.Enterprise.Common.ClientServer.Helper.VersionManager.ModelDataVersionManager");
			var iVersionMgrType = FindTypeOrThrow(assemblies, "Autodesk.RevitServer.Enterprise.Common.ClientServer.Helper.VersionManager.IModelDataVersionManager");
			var sharedUtilsType = FindTypeOrThrow(assemblies, "Autodesk.RevitServer.Enterprise.Common.ClientServer.Helper.Utils.SharedUtils");
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

			var rvtFileType = FindTypeOrThrow(assemblies, "Autodesk.RevitServer.Enterprise.Common.ClientServer.Helper.OleFile.RvtFile");
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
	}
}


