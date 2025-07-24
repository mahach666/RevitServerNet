using System.Threading.Tasks;
using RevitServerNet.Models;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace RevitServerNet.Extensions
{
    // Extensions for working with file history and locks
    public static class HistoryExtensions
    {
        // Gets model history
        public static async Task<ModelHistory> GetModelHistoryAsync(this RevitServerApi api, string modelPath)
        {
            var encodedPath = RevitServerApi.EncodePath(modelPath);
            var command = $"{encodedPath}/history";
            var json = await api.GetAsync(command);
            return DeserializeJson<ModelHistory>(json);
        }

        // Gets the latest version of the model
        public static async Task<HistoryItem> GetLatestVersionAsync(this RevitServerApi api, string modelPath)
        {
            var history = await GetModelHistoryAsync(api, modelPath);
            return history?.Items?.OrderByDescending(h => h.Version).FirstOrDefault();
        }

        // Gets a specific version of the model
        public static async Task<HistoryItem> GetVersionAsync(this RevitServerApi api, string modelPath, int version)
        {
            var history = await GetModelHistoryAsync(api, modelPath);
            return history?.Items?.FirstOrDefault(h => h.Version == version);
        }

        // Gets all versions of the model by user
        public static async Task<List<HistoryItem>> GetVersionsByUserAsync(this RevitServerApi api, string modelPath, string userName)
        {
            var history = await GetModelHistoryAsync(api, modelPath);
            return history?.Items?.Where(h => h.User?.Equals(userName, System.StringComparison.OrdinalIgnoreCase) == true).ToList() 
                   ?? new List<HistoryItem>();
        }

        // Gets the number of versions
        public static async Task<int> GetVersionCountAsync(this RevitServerApi api, string modelPath)
        {
            var history = await GetModelHistoryAsync(api, modelPath);
            return history?.Items?.Count ?? 0;
        }

        // Gets the total size of all versions
        public static async Task<long> GetTotalModelSizeAsync(this RevitServerApi api, string modelPath)
        {
            var history = await GetModelHistoryAsync(api, modelPath);
            return history?.Items?.Sum(h => h.Size + h.SupportSize) ?? 0;
        }

        // Gets all server locks
        public static async Task<LocksList> GetLocksAsync(this RevitServerApi api)
        {
            var json = await api.GetAsync("locks");
            return DeserializeJson<LocksList>(json);
        }

        // Gets model lock info
        public static async Task<LockInfo> GetModelLockAsync(this RevitServerApi api, string modelPath)
        {
            var encodedPath = RevitServerApi.EncodePath(modelPath);
            var command = $"{encodedPath}/lock";
            var json = await api.GetAsync(command);
            return DeserializeJson<LockInfo>(json);
        }

        // Checks if the model is locked
        public static async Task<bool> IsModelLockedAsync(this RevitServerApi api, string modelPath)
        {
            try
            {
                var lockInfo = await GetModelLockAsync(api, modelPath);
                return lockInfo != null && !string.IsNullOrEmpty(lockInfo.UserName);
            }
            catch
            {
                return false;
            }
        }

        // Gets the user who locked the model
        public static async Task<string> GetModelLockUserAsync(this RevitServerApi api, string modelPath)
        {
            var lockInfo = await GetModelLockAsync(api, modelPath);
            return lockInfo?.UserName;
        }

        // Gets all locks by user
        public static async Task<List<LockInfo>> GetLocksByUserAsync(this RevitServerApi api, string userName)
        {
            var allLocks = await GetLocksAsync(api);
            return allLocks?.Locks?.Where(l => l.UserName?.Equals(userName, System.StringComparison.OrdinalIgnoreCase) == true).ToList()
                   ?? new List<LockInfo>();
        }

        // Gets the number of active locks
        public static async Task<int> GetActiveLocksCountAsync(this RevitServerApi api)
        {
            var locks = await GetLocksAsync(api);
            return locks?.Locks?.Count ?? 0;
        }

        // Gets the last version comment
        public static async Task<string> GetLastVersionCommentAsync(this RevitServerApi api, string modelPath)
        {
            var latestVersion = await GetLatestVersionAsync(api, modelPath);
            return latestVersion?.Comment;
        }

        private static T DeserializeJson<T>(string json) where T : class
        {
            if (string.IsNullOrEmpty(json))
                return null;
            return JsonConvert.DeserializeObject<T>(json);
        }
    }
} 