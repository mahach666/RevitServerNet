using System.Threading.Tasks;
using RevitServerNet.Models;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace RevitServerNet.Extensions
{
    // Extensions for working with locks and lock operations
    public static class LockingExtensions
    {
        // Locks a model or folder
        public static async Task<OperationResult> LockItemAsync(this RevitServerApi api, string itemPath)
        {
            var encodedPath = RevitServerApi.EncodePath(itemPath);
            var command = $"{encodedPath}/lock";
            var json = await api.PutAsync(command);
            return DeserializeJson<OperationResult>(json) ?? new OperationResult { Success = !string.IsNullOrEmpty(json) };
        }

        // Unlocks a model or folder
        public static async Task<OperationResult> UnlockItemAsync(this RevitServerApi api, string itemPath)
        {
            var encodedPath = RevitServerApi.EncodePath(itemPath);
            var command = $"{encodedPath}/lock?objectMustExist=true";
            var json = await api.DeleteAsync(command);
            return DeserializeJson<OperationResult>(json) ?? new OperationResult { Success = !string.IsNullOrEmpty(json) };
        }

        // Cancels an in-progress lock
        public static async Task<OperationResult> CancelLockAsync(this RevitServerApi api, string itemPath)
        {
            var encodedPath = RevitServerApi.EncodePath(itemPath);
            var command = $"{encodedPath}/inProgressLock";
            var json = await api.DeleteAsync(command);
            return DeserializeJson<OperationResult>(json) ?? new OperationResult { Success = !string.IsNullOrEmpty(json) };
        }

        // Gets descendent locks
        public static async Task<string> GetDescendentLocksAsync(this RevitServerApi api, string folderPath)
        {
            var encodedPath = RevitServerApi.EncodePath(folderPath);
            var command = $"{encodedPath}/descendent/locks";
            var json = await api.GetAsync(command);
            return json;
        }

        // Deletes descendent locks
        public static async Task<string> DeleteDescendentLocksAsync(this RevitServerApi api, string folderPath)
        {
            var encodedPath = RevitServerApi.EncodePath(folderPath);
            var command = $"{encodedPath}/descendent/locks";
            var json = await api.DeleteAsync(command);
            return json;
        }

        // Copies a model or folder
        public static async Task<OperationResult> CopyItemAsync(this RevitServerApi api, string sourcePath, string destinationPath, bool overwrite = false)
        {
            var encodedSourcePath = RevitServerApi.EncodePath(sourcePath);
            var encodedDestPath = RevitServerApi.EncodePath(destinationPath);
            var command = $"{encodedSourcePath}?destinationObjectPath={encodedDestPath}&pasteAction=Copy&replaceExisting={overwrite.ToString().ToLower()}";
            var json = await api.PostAsync(command);
            return DeserializeJson<OperationResult>(json) ?? new OperationResult { Success = !string.IsNullOrEmpty(json) };
        }

        // Moves a model or folder
        public static async Task<OperationResult> MoveItemAsync(this RevitServerApi api, string sourcePath, string destinationPath, bool overwrite = false)
        {
            var encodedSourcePath = RevitServerApi.EncodePath(sourcePath);
            var encodedDestPath = RevitServerApi.EncodePath(destinationPath);
            var command = $"{encodedSourcePath}?destinationObjectPath={encodedDestPath}&pasteAction=Move&replaceExisting={overwrite.ToString().ToLower()}";
            var json = await api.PostAsync(command);
            return DeserializeJson<OperationResult>(json) ?? new OperationResult { Success = !string.IsNullOrEmpty(json) };
        }

        private static T DeserializeJson<T>(string json) where T : class
        {
            if (string.IsNullOrEmpty(json))
                return null;
            return JsonConvert.DeserializeObject<T>(json);
        }
    }
} 