using Newtonsoft.Json;
using RevitServerNet.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

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
            
            // Check if the response indicates success
            var result = DeserializeJson<OperationResult>(json);
            if (result != null)
                return result;
                
            // If no structured response, check if the item was actually locked
            try
            {
                var folderInfo = await api.GetFolderInfoAsync(itemPath);
                var isLocked = folderInfo?.LockState == LockState.Locked;
                return new OperationResult { Success = isLocked, Message = isLocked ? "Item locked successfully" : "Failed to lock item" };
            }
            catch
            {
                return new OperationResult { Success = false, Message = "Failed to lock item" };
            }
        }

        // Unlocks a model or folder
        public static async Task<OperationResult> UnlockItemAsync(this RevitServerApi api, string itemPath)
        {
            var encodedPath = RevitServerApi.EncodePath(itemPath);
            var command = $"{encodedPath}/lock?objectMustExist=true";
            var json = await api.DeleteAsync(command);
            
            // Check if the response indicates success
            var result = DeserializeJson<OperationResult>(json);
            if (result != null)
                return result;
                
            // If no structured response, check if the item was actually unlocked
            try
            {
                var folderInfo = await api.GetFolderInfoAsync(itemPath);
                var isUnlocked = folderInfo?.LockState == LockState.Unlocked;
                return new OperationResult { Success = isUnlocked, Message = isUnlocked ? "Item unlocked successfully" : "Failed to unlock item" };
            }
            catch
            {
                return new OperationResult { Success = false, Message = "Failed to unlock item" };
            }
        }

        // Cancels an in-progress lock
        public static async Task<OperationResult> CancelLockAsync(this RevitServerApi api, string itemPath)
        {
            try
            {
                var encodedPath = RevitServerApi.EncodePath(itemPath);
                var command = $"{encodedPath}/inProgressLock";
                var json = await api.DeleteAsync(command);
                
                var result = DeserializeJson<OperationResult>(json);
                if (result != null)
                    return result;
                    
                return new OperationResult { Success = !string.IsNullOrEmpty(json), Message = "Lock cancelled" };
            }
            catch (RevitServerApiException ex)
            {
                return new OperationResult { Success = false, Message = $"Failed to cancel lock: {ex.Message}" };
            }
            catch (Exception ex)
            {
                return new OperationResult { Success = false, Message = $"Failed to cancel lock: {ex.Message}" };
            }
        }

        //Gets descendent locks
        public static async Task<string> GetDescendentLocksAsync(this RevitServerApi api, string folderPath)
        {
            try
            {
                var encodedPath = RevitServerApi.EncodePath(folderPath);
                var command = $"{encodedPath}/descendent/locks";
                var json = await api.GetAsync(command);

                //try
                //{
                //    var response = DeserializeJson<dynamic>(json);
                //    if (response?.Items != null)
                //    {
                //        var lockedPaths = new List<string>();
                //        foreach (var item in response.Items)
                //        {
                //            lockedPaths.Add(item.ToString());
                //        }
                //        return lockedPaths;
                //    }
                //}
                //catch
                //{
                //}

                return json;
            }
            catch (RevitServerApiException ex)
            {
                return ex.ToString();
            }
            catch (Exception ex)
            {
                return ex.ToString();
            }
        }

        // Deletes descendent locks
        public static async Task<OperationResult> DeleteDescendentLocksAsync(this RevitServerApi api, string folderPath)
        {
            try
            {
                var encodedPath = RevitServerApi.EncodePath(folderPath);
                var command = $"{encodedPath}/descendent/locks";
                var json = await api.DeleteAsync(command);
                
                var result = DeserializeJson<OperationResult>(json);
                if (result != null)
                    return result;
                    
                return new OperationResult { Success = !string.IsNullOrEmpty(json), Message = "Descendent locks deleted" };
            }
            catch (RevitServerApiException ex)
            {
                return new OperationResult { Success = false, Message = $"Failed to delete descendent locks: {ex.Message}" };
            }
            catch (Exception ex)
            {
                return new OperationResult { Success = false, Message = $"Failed to delete descendent locks: {ex.Message}" };
            }
        }

        // Copies a model or folder
        public static async Task<OperationResult> CopyItemAsync(this RevitServerApi api, string sourcePath, string destinationPath, bool overwrite = false)
        {
            try
            {
                var encodedSourcePath = RevitServerApi.EncodePath(sourcePath);
                var encodedDestPath = RevitServerApi.EncodePath(destinationPath);
                var command = $"{encodedSourcePath}?destinationObjectPath={encodedDestPath}&pasteAction=Copy&replaceExisting={overwrite.ToString().ToLower()}";
                var json = await api.PostAsync(command);
                
                var result = DeserializeJson<OperationResult>(json);
                if (result != null)
                    return result;
                    
                return new OperationResult { Success = !string.IsNullOrEmpty(json), Message = "Item copied successfully" };
            }
            catch (RevitServerApiException ex)
            {
                return new OperationResult { Success = false, Message = $"Failed to copy item: {ex.Message}" };
            }
            catch (Exception ex)
            {
                return new OperationResult { Success = false, Message = $"Failed to copy item: {ex.Message}" };
            }
        }

        // Moves a model or folder
        public static async Task<OperationResult> MoveItemAsync(this RevitServerApi api, string sourcePath, string destinationPath, bool overwrite = false)
        {
            try
            {
                var encodedSourcePath = RevitServerApi.EncodePath(sourcePath);
                var encodedDestPath = RevitServerApi.EncodePath(destinationPath);
                var command = $"{encodedSourcePath}?destinationObjectPath={encodedDestPath}&pasteAction=Move&replaceExisting={overwrite.ToString().ToLower()}";
                var json = await api.PostAsync(command);
                
                var result = DeserializeJson<OperationResult>(json);
                if (result != null)
                    return result;
                    
                return new OperationResult { Success = !string.IsNullOrEmpty(json), Message = "Item moved successfully" };
            }
            catch (RevitServerApiException ex)
            {
                return new OperationResult { Success = false, Message = $"Failed to move item: {ex.Message}" };
            }
            catch (Exception ex)
            {
                return new OperationResult { Success = false, Message = $"Failed to move item: {ex.Message}" };
            }
        }

        private static T DeserializeJson<T>(string json) where T : class
        {
            if (string.IsNullOrEmpty(json))
                return null;
            return JsonConvert.DeserializeObject<T>(json);
        }
    }
} 