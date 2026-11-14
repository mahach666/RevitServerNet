using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitServerNet.Models;
using RevitServerNet.Tools;
using RevitServerNet.Enterprise;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace RevitServerNet.Extensions
{
    // Extensions for working with folders and files
    public static class FolderExtensions
    {
        // Gets the root folder contents
        public static async Task<FolderContents> GetRootFolderContentsAsync(this RevitServerApi api)
        {
            return await GetFolderContentsAsync(api, "|").ConfigureAwait(false);
        }

        // Gets the contents of a folder by path
        public static async Task<FolderContents> GetFolderContentsAsync(this RevitServerApi api, string folderPath)
        {
            var encodedPath = RevitServerApi.EncodePath(NormalizePath(folderPath));
            var command = $"{encodedPath}/contents";
            var json = await api.GetAsync(command).ConfigureAwait(false);
            return DeserializeJson<FolderContents>(json);
        }

        // Gets folder info (DirectoryInfo endpoint)
        public static async Task<FolderInfo> GetFolderInfoAsync(this RevitServerApi api, string folderPath)
        {
            var encodedPath = RevitServerApi.EncodePath(NormalizePath(folderPath));
            var command = $"{encodedPath}/directoryinfo";
            var json = await api.GetAsync(command).ConfigureAwait(false);
            return DeserializeJson<FolderInfo>(json);
        }

        // Creates a new folder. The server answers 201 (200 if it already exists) with an empty body: both are Success.
        // Some names (for example "Кир") make the server answer 500 after it created the folder: after a 5xx (or no answer) the outcome
        // is checked, and the folder is reported as created when it exists. Any other HTTP error is thrown as RevitServerApiException.
        public static async Task<OperationResult> CreateFolderAsync(this RevitServerApi api, string parentFolderPath, string folderName)
        {
            if (string.IsNullOrWhiteSpace(folderName))
                throw new ArgumentException("Folder name is required", nameof(folderName));
            if (folderName.IndexOfAny(new[] { '|', '/', '\\' }) >= 0)
                throw new ArgumentException("Folder name must be a name, not a path", nameof(folderName));

            var path = $"{parentFolderPath}/{folderName}";
            try
            {
                await api.PutAsync(RevitServerApi.EncodePath(path)).ConfigureAwait(false);
            }
            catch (RevitServerApiException ex) when (OutcomeUnknown(ex))
            {
                if (await VerifyAsync(() => FolderExistsAsync(api, path)).ConfigureAwait(false))
                    return new OperationResult { Success = true, Message = $"Folder created ({Answer(ex)} but the folder exists)" };
                throw;
            }

            return new OperationResult { Success = true, Message = "Folder created successfully" };
        }

        // Deletes a folder (or a model) with all its contents: the server's delete is DELETE <path>?newObjectName= with an empty name.
        // The root is never deleted. HTTP errors (404 not found, 403 locked, ...) are returned as Success=false with the status in the message;
        // after a 5xx the outcome is checked and the item is reported as deleted when it is gone.
        public static async Task<OperationResult> DeleteFolderAsync(this RevitServerApi api, string folderPath)
        {
            if (IsRoot(folderPath))
                return new OperationResult { Success = false, Message = "Cannot delete root folder" };

            var path = NormalizePath(folderPath);
            try
            {
                await api.DeleteAsync($"{RevitServerApi.EncodePath(path)}?newObjectName=").ConfigureAwait(false);
            }
            catch (RevitServerApiException ex)
            {
                if (OutcomeUnknown(ex) && await VerifyAsync(async () => await TryGetDirectoryInfoAsync(api, path).ConfigureAwait(false) == null).ConfigureAwait(false))
                    return new OperationResult { Success = true, Message = $"Folder deleted ({Answer(ex)} but the folder is gone)" };
                return new OperationResult { Success = false, Message = $"Failed to delete folder: {ex.Message}" };
            }

            return new OperationResult { Success = true, Message = "Folder deleted successfully" };
        }

        // Renames a folder. The server's rename is DELETE <path>?newObjectName=<name>, and an empty name there DELETES the item:
        // a null, empty or whitespace newName (or one with a path separator) throws ArgumentException and no request is sent.
        // The server answers 201 with an empty body; HTTP errors (409 name taken, 403 locked, 404, ...) are returned as Success=false
        // with the status in the message; after a 5xx the outcome is checked in the parent folder.
        public static async Task<OperationResult> RenameFolderAsync(this RevitServerApi api, string folderPath, string newName)
        {
            if (string.IsNullOrWhiteSpace(newName))
                throw new ArgumentException("New folder name is required: an empty name would delete the folder", nameof(newName));
            if (newName.IndexOfAny(new[] { '|', '/', '\\' }) >= 0)
                throw new ArgumentException("New folder name must be a name, not a path", nameof(newName));
            if (IsRoot(folderPath))
                return new OperationResult { Success = false, Message = "Cannot rename root folder" };

            var path = NormalizePath(folderPath);
            try
            {
                await api.DeleteAsync($"{RevitServerApi.EncodePath(path)}?newObjectName={Uri.EscapeDataString(newName)}").ConfigureAwait(false);
            }
            catch (RevitServerApiException ex)
            {
                if (OutcomeUnknown(ex) && await VerifyAsync(() => WasRenamedAsync(api, path, newName)).ConfigureAwait(false))
                    return new OperationResult { Success = true, Message = $"Folder renamed ({Answer(ex)} but the folder has the new name)" };
                return new OperationResult { Success = false, Message = $"Failed to rename folder: {ex.Message}" };
            }

            return new OperationResult { Success = true, Message = "Folder renamed successfully" };
        }

        // The parent folder lists newName (exact case) and no longer lists the old name (unless only the case changed)
        private static async Task<bool> WasRenamedAsync(RevitServerApi api, string oldPath, string newName)
        {
            var split = oldPath.LastIndexOf('|');
            var oldName = oldPath.Substring(split + 1);
            var parent = split <= 0 ? "|" : oldPath.Substring(0, split);
            var contents = await GetFolderContentsAsync(api, parent).ConfigureAwait(false);
            var names = new List<string>();
            if (contents?.Folders != null) foreach (var f in contents.Folders) names.Add(f.Name);
            if (contents?.Models != null) foreach (var m in contents.Models) names.Add(m.Name);
            var onlyCaseChanged = string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase);
            return names.Contains(newName)
                && (onlyCaseChanged || !names.Exists(n => string.Equals(n, oldName, StringComparison.OrdinalIgnoreCase)));
        }

        // Gets model info
        public static async Task<ModelInfo> GetModelInfoAsync(this RevitServerApi api, string modelPath)
        {
            var encodedPath = RevitServerApi.EncodePath(modelPath);
            var command = $"{encodedPath}/modelinfo";
            var json = await api.GetAsync(command).ConfigureAwait(false);
            return DeserializeJson<ModelInfo>(json);
        }

        // Gets folder size
        public static async Task<long> GetFolderSizeAsync(this RevitServerApi api, string folderPath)
        {
            var folderInfo = await GetFolderInfoAsync(api, folderPath).ConfigureAwait(false);
            return folderInfo?.Size ?? 0;
        }

        // Checks if folder exists: false for 404 and for a path that is a model (directoryinfo answers 200 with IsFolder=false for it).
        // Any other failure (timeout, 401/403, 5xx) is thrown as RevitServerApiException, it does not mean "does not exist".
        public static async Task<bool> FolderExistsAsync(this RevitServerApi api, string folderPath)
        {
            var info = await TryGetDirectoryInfoAsync(api, folderPath).ConfigureAwait(false);
            return info != null && info.Value<bool?>("IsFolder") != false;
        }

        // Checks if model exists: false for 404 (also for a folder path). Any other failure is thrown as RevitServerApiException.
        public static async Task<bool> ModelExistsAsync(this RevitServerApi api, string modelPath)
        {
            try
            {
                await GetModelInfoAsync(api, modelPath).ConfigureAwait(false);
                return true;
            }
            catch (RevitServerApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return false;
            }
        }

        // directoryinfo as JSON; null when the server answers 404 (no such item, the body then has Exists=false). It answers 200 for models too.
        private static async Task<JObject> TryGetDirectoryInfoAsync(RevitServerApi api, string path)
        {
            try
            {
                var json = await api.GetAsync($"{RevitServerApi.EncodePath(NormalizePath(path))}/directoryinfo").ConfigureAwait(false);
                var info = string.IsNullOrWhiteSpace(json) ? new JObject() : JObject.Parse(json);
                return info.Value<bool?>("Exists") == false ? null : info;
            }
            catch (RevitServerApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        internal static bool IsServerError(RevitServerApiException ex) => ex.StatusCode.HasValue && (int)ex.StatusCode.Value >= 500;

        // The server may have done the operation: it answered 5xx (it does so after some operations it performed),
        // or no answer came (timeout, connection lost after sending)
        internal static bool OutcomeUnknown(RevitServerApiException ex) => !ex.StatusCode.HasValue || IsServerError(ex);

        internal static string Answer(RevitServerApiException ex) =>
            ex.StatusCode.HasValue ? $"the server answered {(int)ex.StatusCode.Value}" : "no answer came from the server";

        // A folder or a model exists at the path (directoryinfo answers for both); false for 404. Other failures are thrown.
        internal static async Task<bool> ItemExistsAsync(RevitServerApi api, string path)
            => await TryGetDirectoryInfoAsync(api, path).ConfigureAwait(false) != null;

        // Outcome check after a 5xx: a failing check means "not verified", the original error is then reported
        internal static async Task<bool> VerifyAsync(Func<Task<bool>> check)
        {
            try { return await check().ConfigureAwait(false); }
            catch (RevitServerApiException) { return false; }
            catch (Newtonsoft.Json.JsonException) { return false; } // an answer that is not JSON verifies nothing
        }

        // True for the root: null, empty, whitespace or only separators ("|", "/", "||", ...)
        private static bool IsRoot(string path) => string.IsNullOrWhiteSpace(path) || NormalizePath(path) == "|";

        // Pipe path in the form of EncodePath (one '|' between names, leading '|') with raw names and without a trailing '|'; the root is "|"
        internal static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return "|";
            path = path.Replace('\\', '|').Replace('/', '|');
            while (path.Contains("||"))
                path = path.Replace("||", "|");
            if (!path.StartsWith("|"))
                path = "|" + path;
            var trimmed = path.TrimEnd('|');
            return trimmed.Length > 0 ? trimmed : "|";
        }

        private static string Join(string parentPath, string name) => parentPath == "|" ? "|" + name : parentPath + "|" + name;

        // Recursively gets all models in a folder (a missing or unreadable folder throws RevitServerApiException)
        public static async Task<List<ModelInfo>> GetAllModelsRecursiveAsync(this RevitServerApi api, string folderPath = "|")
        {
            var models = new List<ModelInfo>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await GetModelsRecursiveInternal(api, NormalizePath(folderPath), models, visited).ConfigureAwait(false);
            return models;
        }

        // Recursively gets all folders (a missing or unreadable folder throws RevitServerApiException)
        public static async Task<List<FolderInfo>> GetAllFoldersRecursiveAsync(this RevitServerApi api, string folderPath = "|")
        {
            var folders = new List<FolderInfo>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await GetFoldersRecursiveInternal(api, NormalizePath(folderPath), folders, visited).ConfigureAwait(false);
            return folders;
        }

        // Internal recursive model collector
        private static async Task GetModelsRecursiveInternal(RevitServerApi api, string folderPath, List<ModelInfo> models, HashSet<string> visited)
        {
            if (!visited.Add(folderPath)) return;
            var contents = await GetFolderContentsAsync(api, folderPath).ConfigureAwait(false);
            // A folder that could not be read must not silently drop out: callers treat the result as the full list
            if (contents == null)
                throw new RevitServerApiException($"Empty response for folder contents: {folderPath}");
            if (contents.Models != null)
            {
                foreach (var model in contents.Models)
                {
                    // Set the correct path for the model based on current folder path
                    model.Path = Join(folderPath, model.Name);
                    models.Add(model);
                }
            }
            if (contents.Folders != null)
            {
                foreach (var folder in contents.Folders)
                    await GetModelsRecursiveInternal(api, Join(folderPath, folder.Name), models, visited).ConfigureAwait(false);
            }
        }

        // Internal recursive folder collector
        private static async Task GetFoldersRecursiveInternal(RevitServerApi api, string folderPath, List<FolderInfo> folders, HashSet<string> visited)
        {
            if (!visited.Add(folderPath)) return;
            var contents = await GetFolderContentsAsync(api, folderPath).ConfigureAwait(false);
            if (contents == null)
                throw new RevitServerApiException($"Empty response for folder contents: {folderPath}");
            if (contents.Folders == null) return;
            foreach (var folder in contents.Folders)
            {
                folder.Path = Join(folderPath, folder.Name);
            }
            folders.AddRange(contents.Folders);
            foreach (var folder in contents.Folders)
                await GetFoldersRecursiveInternal(api, folder.Path, folders, visited).ConfigureAwait(false);
        }

        // Gets only files in a folder
        public static async Task<List<RevitFileInfo>> ListFilesAsync(this RevitServerApi api, string folderPath = "|")
        {
            var contents = await GetFolderContentsAsync(api, folderPath).ConfigureAwait(false);
            return contents?.Files ?? new List<RevitFileInfo>();
        }

        // Gets only folders in a folder
        public static async Task<List<FolderInfo>> ListFoldersAsync(this RevitServerApi api, string folderPath = "|")
        {
            var contents = await GetFolderContentsAsync(api, folderPath).ConfigureAwait(false);
            if (contents?.Folders == null) return new List<FolderInfo>();
            var parent = NormalizePath(folderPath);
            foreach (var folder in contents.Folders)
            {
                folder.Path = Join(parent, folder.Name);
            }
            return contents.Folders;
        }

        // Gets only models in a folder
        public static async Task<List<ModelInfo>> ListModelsAsync(this RevitServerApi api, string folderPath = "|")
        {
            var contents = await GetFolderContentsAsync(api, folderPath).ConfigureAwait(false);
            if (contents?.Models == null) return new List<ModelInfo>();

            var parent = NormalizePath(folderPath);
            var models = new List<ModelInfo>();
            foreach (var model in contents.Models)
            {
                // Set the correct path for the model based on current folder path
                model.Path = Join(parent, model.Name);
                models.Add(model);
            }
            return models;
        }

        // Recursively walks the folder tree. A folder that cannot be read (404, 500, auth, network) throws RevitServerApiException:
        // the result is never silently partial. digModels also walks into the model "folders" (their files and Data/Permissions folders are
        // listed after the model path); the model path itself is listed once, as a model.
        public static async Task<WalkResult> WalkAsync(this RevitServerApi api, string topPath = "|", bool includeFiles = true, bool includeModels = true, bool digModels = false)
        {
            var result = new WalkResult();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await WalkRecursiveInternal(api, NormalizePath(topPath), includeFiles, includeModels, digModels, false, result, visited).ConfigureAwait(false);
            return result;
        }

        // Internal recursive walk
        private static async Task WalkRecursiveInternal(RevitServerApi api, string currentPath, bool includeFiles, bool includeModels, bool digModels, bool isModel, WalkResult result, HashSet<string> visited)
        {
            if (!visited.Add(currentPath)) return;
            var contents = await GetFolderContentsAsync(api, currentPath).ConfigureAwait(false);
            if (contents == null)
                throw new RevitServerApiException($"Empty response for folder contents: {currentPath}");
            if (!isModel)
            {
                result.AllPaths.Add(currentPath);
                result.FolderPaths.Add(currentPath);
            }
            if (includeFiles && contents.Files != null)
            {
                foreach (var file in contents.Files)
                {
                    var filePath = Join(currentPath, file.Name);
                    result.AllPaths.Add(filePath);
                    result.FilePaths.Add(filePath);
                }
            }
            if (includeModels && contents.Models != null)
            {
                foreach (var model in contents.Models)
                {
                    var modelPath = Join(currentPath, model.Name);
                    result.AllPaths.Add(modelPath);
                    result.ModelPaths.Add(modelPath);
                }
            }
            if (contents.Folders != null)
            {
                foreach (var folder in contents.Folders)
                    await WalkRecursiveInternal(api, Join(currentPath, folder.Name), includeFiles, includeModels, digModels, false, result, visited).ConfigureAwait(false);
            }
            if (digModels && includeModels && contents.Models != null)
            {
                foreach (var model in contents.Models)
                    await WalkRecursiveInternal(api, Join(currentPath, model.Name), includeFiles, includeModels, digModels, true, result, visited).ConfigureAwait(false);
            }
        }

        private static T DeserializeJson<T>(string json) where T : class
        {
            if (string.IsNullOrEmpty(json))
                return null;
            return JsonConvert.DeserializeObject<T>(json);
        }

        // Creates a local RVT model through the Revit Server ModelService directly (no RevitServerTool.exe)
        public static Task<string> CreateLocalModelWithoutToolAsync(this RevitServerApi api, string modelPipePath, string destinationFile, string revitVersion = null, string assembliesPath = null, bool overwrite = false)
        {
            // createLocal: false keeps the behaviour of this signature before 1.3.0 (the file keeps the server's BasicFileInfo).
            return CreateLocalModelWithoutToolAsync(api, modelPipePath, destinationFile, false, revitVersion, assembliesPath, overwrite, null, CancellationToken.None);
        }

        // Same as above; createLocal writes the BasicFileInfo of a local copy as RevitServerTool does (ModelExporterOptions.CreateLocal)
        public static async Task<string> CreateLocalModelWithoutToolAsync(this RevitServerApi api, string modelPipePath, string destinationFile, bool createLocal, string revitVersion = null, string assembliesPath = null, bool overwrite = false, IProgress<long> bytesProgress = null, CancellationToken cancellationToken = default)
        {
            if (api == null)
                throw new ArgumentNullException(nameof(api));
            if (string.IsNullOrWhiteSpace(modelPipePath))
                throw new ArgumentException("Model pipe path is required", nameof(modelPipePath));
            if (string.IsNullOrWhiteSpace(destinationFile))
                throw new ArgumentException("Destination file is required", nameof(destinationFile));

            var exporter = new RsModelExporter();
            var options = CreateLocalOptions(api, modelPipePath, destinationFile, createLocal, revitVersion, assembliesPath, overwrite);

            await exporter.ExportAsync(options, bytesProgress, cancellationToken).ConfigureAwait(false);
            return destinationFile;
        }

        // Export options of CreateLocalModelWithoutToolAsync: host of BaseUrl without the HTTP port; year: revitVersion, else the year in BaseUrl, else "2022"
        internal static RsModelExporterOptions CreateLocalOptions(RevitServerApi api, string modelPipePath, string destinationFile, bool createLocal, string revitVersion, string assembliesPath, bool overwrite)
        {
            var version = revitVersion ?? VersionUtils.ParseVersionFromBaseUrl(api.BaseUrl) ?? "2022";
            var host = new Uri(api.BaseUrl).Host;

            return new RsModelExporterOptions
            {
                ServerHost = host,
                ModelPipePath = modelPipePath,
                DestinationFile = destinationFile,
                RevitVersion = version,
                AssembliesPath = assembliesPath,
                Overwrite = overwrite,
                CreateLocal = createLocal
            };
        }
    }
} 