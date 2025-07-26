using System;
using System.Threading.Tasks;
using RevitServerNet.Models;
using System.Collections.Generic;
using System.Diagnostics;
using Newtonsoft.Json;

namespace RevitServerNet.Extensions
{
    // Extensions for working with folders and files
    public static class FolderExtensions
    {
        // Gets the root folder contents
        public static async Task<FolderContents> GetRootFolderContentsAsync(this RevitServerApi api)
        {
            return await GetFolderContentsAsync(api, "|");
        }

        // Gets the contents of a folder by path
        public static async Task<FolderContents> GetFolderContentsAsync(this RevitServerApi api, string folderPath)
        {
            var encodedPath = RevitServerApi.EncodePath(folderPath);
            var command = $"{encodedPath}/contents";
            var json = await api.GetAsync(command);
            return DeserializeJson<FolderContents>(json);
        }

        // Gets folder info (DirectoryInfo endpoint)
        public static async Task<FolderInfo> GetFolderInfoAsync(this RevitServerApi api, string folderPath)
        {
            var encodedPath = RevitServerApi.EncodePath(folderPath);
            var command = $"{encodedPath}/directoryinfo";
            var json = await api.GetAsync(command);
            return DeserializeJson<FolderInfo>(json);
        }

        // Creates a new folder
        public static async Task<OperationResult> CreateFolderAsync(this RevitServerApi api, string parentFolderPath, string folderName)
        {
            var encodedPath = RevitServerApi.EncodePath($"{parentFolderPath}/{folderName}");
            var command = $"{encodedPath}"; // Empty command for PUT request
            var json = await api.PutAsync(command);
            
            // Check if the response indicates success
            var result = DeserializeJson<OperationResult>(json);
            if (result != null)
                return result;
                
            // If no structured response, check if the folder was actually created
            try
            {
                var exists = await FolderExistsAsync(api, $"{parentFolderPath}/{folderName}");
                return new OperationResult { Success = exists, Message = exists ? "Folder created successfully" : "Failed to create folder" };
            }
            catch
            {
                return new OperationResult { Success = false, Message = "Failed to create folder" };
            }
        }

        // Deletes a folder
        public static async Task<OperationResult> DeleteFolderAsync(this RevitServerApi api, string folderPath)
        {
            try
            {
                // Don't allow deletion of root folder
                if (folderPath == "|" || string.IsNullOrEmpty(folderPath))
                {
                    return new OperationResult { Success = false, Message = "Cannot delete root folder" };
                }
                
                var encodedPath = RevitServerApi.EncodePath(folderPath);
                var command = $"{encodedPath}"; // Empty command for DELETE request
                var json = await api.DeleteAsync(command);
                
                // Check if the response indicates success
                var result = DeserializeJson<OperationResult>(json);
                if (result != null)
                    return result;
                    
                // If no structured response, check if the folder was actually deleted
                try
                {
                    var exists = await FolderExistsAsync(api, folderPath);
                    return new OperationResult { Success = !exists, Message = exists ? "Failed to delete folder" : "Folder deleted successfully" };
                }
                catch
                {
                    return new OperationResult { Success = false, Message = "Failed to delete folder" };
                }
            }
            catch (RevitServerApiException ex) when (ex.Message.Contains("MethodNotAllowed"))
            {
                return new OperationResult { Success = false, Message = "Delete operation not allowed - may require special permissions" };
            }
            catch (Exception ex)
            {
                return new OperationResult { Success = false, Message = $"Failed to delete folder: {ex.Message}" };
            }
        }

        // Renames a folder
        public static async Task<OperationResult> RenameFolderAsync(this RevitServerApi api, string folderPath, string newName)
        {
            try
            {
                // Don't allow renaming of root folder
                if (folderPath == "|" || string.IsNullOrEmpty(folderPath))
                {
                    return new OperationResult { Success = false, Message = "Cannot rename root folder" };
                }
                
                var encodedPath = RevitServerApi.EncodePath(folderPath);
                var command = $"{encodedPath}?newObjectName={newName}";
                var json = await api.DeleteAsync(command);
                
                // Check if the response indicates success
                var result = DeserializeJson<OperationResult>(json);
                if (result != null)
                    return result;
                    
                // If no structured response, check if the folder was actually renamed
                try
                {
                    var parentPath = folderPath.Substring(0, folderPath.LastIndexOf('|'));
                    if (string.IsNullOrEmpty(parentPath)) parentPath = "|";
                    
                    var oldExists = await FolderExistsAsync(api, folderPath);
                    var newExists = await FolderExistsAsync(api, $"{parentPath}/{newName}");
                    
                    return new OperationResult { 
                        Success = !oldExists && newExists, 
                        Message = (!oldExists && newExists) ? "Folder renamed successfully" : "Failed to rename folder" 
                    };
                }
                catch
                {
                    return new OperationResult { Success = false, Message = "Failed to rename folder" };
                }
            }
            catch (RevitServerApiException ex) when (ex.Message.Contains("MethodNotAllowed"))
            {
                return new OperationResult { Success = false, Message = "Rename operation not allowed - may require special permissions" };
            }
            catch (Exception ex)
            {
                return new OperationResult { Success = false, Message = $"Failed to rename folder: {ex.Message}" };
            }
        }

        // Gets model info
        public static async Task<ModelInfo> GetModelInfoAsync(this RevitServerApi api, string modelPath)
        {
            var encodedPath = RevitServerApi.EncodePath(modelPath);
            var command = $"{encodedPath}/modelinfo";
            var json = await api.GetAsync(command);
            return DeserializeJson<ModelInfo>(json);
        }

        // Gets folder size
        public static async Task<long> GetFolderSizeAsync(this RevitServerApi api, string folderPath)
        {
            var folderInfo = await GetFolderInfoAsync(api, folderPath);
            return folderInfo?.Size ?? 0;
        }

        // Checks if folder exists
        public static async Task<bool> FolderExistsAsync(this RevitServerApi api, string folderPath)
        {
            try { await GetFolderInfoAsync(api, folderPath); return true; } catch { return false; }
        }

        // Checks if model exists
        public static async Task<bool> ModelExistsAsync(this RevitServerApi api, string modelPath)
        {
            try { await GetModelInfoAsync(api, modelPath); return true; } catch { return false; }
        }

        // Recursively gets all models in a folder
        public static async Task<List<ModelInfo>> GetAllModelsRecursiveAsync(this RevitServerApi api, string folderPath = "|")
        {
            var models = new List<ModelInfo>();
            var visited = new HashSet<string>();
            await GetModelsRecursiveInternal(api, folderPath, models, visited);
            return models;
        }

        // Recursively gets all folders
        public static async Task<List<FolderInfo>> GetAllFoldersRecursiveAsync(this RevitServerApi api, string folderPath = "|")
        {
            var folders = new List<FolderInfo>();
            var visited = new HashSet<string>();
            await GetFoldersRecursiveInternal(api, folderPath, folders, visited);
            return folders;
        }

        // Internal recursive model collector
        private static async Task GetModelsRecursiveInternal(RevitServerApi api, string folderPath, List<ModelInfo> models, HashSet<string> visited)
        {
            if (string.IsNullOrEmpty(folderPath) || visited.Contains(folderPath)) return;
            visited.Add(folderPath);
            var contents = await GetFolderContentsAsync(api, folderPath);
            if (contents == null) return;
            if (contents.Models != null) models.AddRange(contents.Models);
            if (contents.Folders != null)
            {
                foreach (var folder in contents.Folders)
                {
                    var childPath = folderPath == "|" ? "|" + folder.Name : folderPath + "|" + folder.Name;
                    if (!string.IsNullOrEmpty(childPath) && childPath != folderPath)
                        await GetModelsRecursiveInternal(api, childPath, models, visited);
                }
            }
        }

        // Internal recursive folder collector
        private static async Task GetFoldersRecursiveInternal(RevitServerApi api, string folderPath, List<FolderInfo> folders, HashSet<string> visited)
        {
            if (string.IsNullOrEmpty(folderPath) || visited.Contains(folderPath)) return;
            visited.Add(folderPath);
            var contents = await GetFolderContentsAsync(api, folderPath);
            if (contents?.Folders == null) return;
            folders.AddRange(contents.Folders);
            foreach (var folder in contents.Folders)
            {
                var childPath = folderPath == "|" ? "|" + folder.Name : folderPath + "|" + folder.Name;
                if (!string.IsNullOrEmpty(childPath) && childPath != folderPath)
                    await GetFoldersRecursiveInternal(api, childPath, folders, visited);
            }
        }

        // Gets only files in a folder
        public static async Task<List<RevitFileInfo>> ListFilesAsync(this RevitServerApi api, string folderPath = "|")
        {
            var contents = await GetFolderContentsAsync(api, folderPath);
            return contents?.Files ?? new List<RevitFileInfo>();
        }

        // Gets only folders in a folder
        public static async Task<List<FolderInfo>> ListFoldersAsync(this RevitServerApi api, string folderPath = "|")
        {
            var contents = await GetFolderContentsAsync(api, folderPath);
            return contents?.Folders ?? new List<FolderInfo>();
        }

        // Gets only models in a folder
        public static async Task<List<ModelInfo>> ListModelsAsync(this RevitServerApi api, string folderPath = "|")
        {
            var contents = await GetFolderContentsAsync(api, folderPath);
            return contents?.Models ?? new List<ModelInfo>();
        }

        // Recursively walks the folder tree (like Python walk)
        public static async Task<WalkResult> WalkAsync(this RevitServerApi api, string topPath = "|", bool includeFiles = true, bool includeModels = true, bool digModels = false)
        {
            var result = new WalkResult();
            var visited = new HashSet<string>();
            await WalkRecursiveInternal(api, topPath, includeFiles, includeModels, digModels, result, visited);
            return result;
        }

        // Internal recursive walk
        private static async Task WalkRecursiveInternal(RevitServerApi api, string currentPath, bool includeFiles, bool includeModels, bool digModels, WalkResult result, HashSet<string> visited)
        {
            if (string.IsNullOrEmpty(currentPath) || visited.Contains(currentPath)) return;
            visited.Add(currentPath);
            try
            {
                var contents = await GetFolderContentsAsync(api, currentPath);
                if (contents == null) return;
                result.AllPaths.Add(currentPath);
                result.FolderPaths.Add(currentPath);
                if (includeFiles && contents.Files != null)
                {
                    foreach (var file in contents.Files)
                    {
                        var filePath = currentPath == "|" ? "|" + file.Name : currentPath + "|" + file.Name;
                        result.AllPaths.Add(filePath);
                        result.FilePaths.Add(filePath);
                    }
                }
                if (includeModels && contents.Models != null)
                {
                    foreach (var model in contents.Models)
                    {
                        var modelPath = currentPath == "|" ? "|" + model.Name : currentPath + "|" + model.Name;
                        result.AllPaths.Add(modelPath);
                        result.ModelPaths.Add(modelPath);
                    }
                }
                if (contents.Folders != null)
                {
                    foreach (var folder in contents.Folders)
                    {
                        var childPath = currentPath == "|" ? "|" + folder.Name : currentPath + "|" + folder.Name;
                        if (!string.IsNullOrEmpty(childPath) && childPath != currentPath)
                            await WalkRecursiveInternal(api, childPath, includeFiles, includeModels, digModels, result, visited);
                    }
                }
                if (digModels && includeModels && contents.Models != null)
                {
                    foreach (var model in contents.Models)
                    {
                        var modelPath = currentPath == "|" ? "|" + model.Name : currentPath + "|" + model.Name;
                        if (!string.IsNullOrEmpty(modelPath) && modelPath != currentPath)
                            await WalkRecursiveInternal(api, modelPath, includeFiles, includeModels, digModels, result, visited);
                    }
                }
            }
            catch { }
        }

        private static T DeserializeJson<T>(string json) where T : class
        {
            if (string.IsNullOrEmpty(json))
                return null;
            return JsonConvert.DeserializeObject<T>(json);
        }
    }
} 