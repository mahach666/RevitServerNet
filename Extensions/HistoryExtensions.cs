using Newtonsoft.Json;
using RevitServerNet.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RevitServerNet.Extensions
{
    // Extensions for working with file history and locks
    public static class HistoryExtensions
    {
        // NOTE: Revit Server Admin REST history endpoint does not expose paging.
        // All limit/filter helpers below are client-side only.
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

        // Gets model history with client-side filtering and limiting
        public static async Task<ModelHistory> GetModelHistoryAsync(
            this RevitServerApi api,
            string modelPath,
            int? take = null,
            int? skip = null,
            string userFilter = null,
            int? minVersion = null,
            int? maxVersion = null,
            DateTime? fromDate = null,
            DateTime? toDate = null)
        {
            var history = await GetModelHistoryAsync(api, modelPath);
            if (history?.Items == null) return history;

            IEnumerable<HistoryItem> query = history.Items;

            if (!string.IsNullOrWhiteSpace(userFilter))
            {
                query = query.Where(h => h.User?.Equals(userFilter, StringComparison.OrdinalIgnoreCase) == true);
            }
            if (minVersion.HasValue)
            {
                query = query.Where(h => h.Version >= minVersion.Value);
            }
            if (maxVersion.HasValue)
            {
                query = query.Where(h => h.Version <= maxVersion.Value);
            }
            if (fromDate.HasValue || toDate.HasValue)
            {
                query = query.Where(h =>
                {
                    if (!DateTime.TryParse(h.Date, out var parsed)) return false;
                    if (fromDate.HasValue && parsed < fromDate.Value) return false;
                    if (toDate.HasValue && parsed > toDate.Value) return false;
                    return true;
                });
            }

            // Order by version descending by default for paging the latest entries first
            query = query.OrderByDescending(h => h.Version);

            if (skip.HasValue && skip.Value > 0)
            {
                query = query.Skip(skip.Value);
            }
            if (take.HasValue && take.Value > 0)
            {
                query = query.Take(take.Value);
            }

            return new ModelHistory
            {
                Path = history.Path,
                Items = query.ToList()
            };
        }

        // Gets the latest N versions (client-side)
        public static async Task<List<HistoryItem>> GetLatestVersionsAsync(this RevitServerApi api, string modelPath, int count)
        {
            var history = await GetModelHistoryAsync(api, modelPath);
            return history?.Items?.OrderByDescending(h => h.Version).Take(Math.Max(count, 0)).ToList() ?? new List<HistoryItem>();
        }

        // Gets a page of history (skip/take, client-side)
        public static async Task<List<HistoryItem>> GetModelHistoryPageAsync(this RevitServerApi api, string modelPath, int skip, int take)
        {
            var history = await GetModelHistoryAsync(api, modelPath);
            return history?.Items?.OrderByDescending(h => h.Version).Skip(Math.Max(skip, 0)).Take(Math.Max(take, 0)).ToList() ?? new List<HistoryItem>();
        }

        // Gets all versions by user with optional limiting (client-side)
        public static async Task<List<HistoryItem>> GetVersionsByUserAsync(this RevitServerApi api, string modelPath, string userName, int? take)
        {
            var history = await GetModelHistoryAsync(api, modelPath);
            var query = history?.Items?.Where(h => h.User?.Equals(userName, StringComparison.OrdinalIgnoreCase) == true)
                        .OrderByDescending(h => h.Version) ?? Enumerable.Empty<HistoryItem>();
            if (take.HasValue && take.Value > 0)
                query = query.Take(take.Value);
            return query.ToList();
        }

        private static T DeserializeJson<T>(string json) where T : class
        {
            if (string.IsNullOrEmpty(json))
                return null;
            return JsonConvert.DeserializeObject<T>(json);
        }
    }
} 