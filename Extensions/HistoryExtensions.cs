using Newtonsoft.Json;
using RevitServerNet.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
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
            var json = await api.GetAsync(command).ConfigureAwait(false);
            return DeserializeJson<ModelHistory>(json);
        }

        // Gets the latest version of the model
        public static async Task<HistoryItem> GetLatestVersionAsync(this RevitServerApi api, string modelPath)
        {
            var history = await GetModelHistoryAsync(api, modelPath).ConfigureAwait(false);
            return history?.Items?.OrderByDescending(h => h.Version).FirstOrDefault();
        }

        // Gets a specific version of the model
        public static async Task<HistoryItem> GetVersionAsync(this RevitServerApi api, string modelPath, int version)
        {
            var history = await GetModelHistoryAsync(api, modelPath).ConfigureAwait(false);
            return history?.Items?.FirstOrDefault(h => h.Version == version);
        }

        // Gets all versions of the model by user
        public static async Task<List<HistoryItem>> GetVersionsByUserAsync(this RevitServerApi api, string modelPath, string userName)
        {
            var history = await GetModelHistoryAsync(api, modelPath).ConfigureAwait(false);
            return history?.Items?.Where(h => h.User?.Equals(userName, System.StringComparison.OrdinalIgnoreCase) == true).ToList() 
                   ?? new List<HistoryItem>();
        }

        // Gets the number of versions
        public static async Task<int> GetVersionCountAsync(this RevitServerApi api, string modelPath)
        {
            var history = await GetModelHistoryAsync(api, modelPath).ConfigureAwait(false);
            return history?.Items?.Count ?? 0;
        }

        // Gets the total size of all versions
        public static async Task<long> GetTotalModelSizeAsync(this RevitServerApi api, string modelPath)
        {
            var history = await GetModelHistoryAsync(api, modelPath).ConfigureAwait(false);
            return history?.Items?.Sum(h => h.Size + h.SupportSize) ?? 0;
        }

        // Gets all server locks: the models and folders that are locked, from GET |/descendent/locks.
        // The server does not return the owner of a lock there, so LockInfo.UserName is null.
        public static async Task<LocksList> GetLocksAsync(this RevitServerApi api)
        {
            var json = await api.GetAsync(RevitServerApi.EncodePath("|") + "/descendent/locks").ConfigureAwait(false);
            var response = DeserializeJson<DescendentLocks>(json)
                ?? throw new RevitServerApiException("Empty response of descendent/locks");
            return new LocksList
            {
                Locks = (response.Items ?? new List<string>())
                    .Select(path => new LockInfo { ModelPath = path, LockState = LockState.Locked })
                    .ToList()
            };
        }

        // Gets model lock info from directoryinfo of the model; null when the model is not locked.
        // The server returns the owner only for locks in progress (ModelLocksInProgress); otherwise UserName is null.
        public static async Task<LockInfo> GetModelLockAsync(this RevitServerApi api, string modelPath)
        {
            var info = await api.GetFolderInfoAsync(modelPath).ConfigureAwait(false)
                ?? throw new RevitServerApiException("Empty response of directoryinfo for '" + modelPath + "'");
            // RS 2022/2025 answer a missing model or folder with 404 (thrown by GetFolderInfoAsync); a 200 with Exists=false is treated alike
            if (info.Exists == false)
                throw new RevitServerApiException("Item not found: '" + modelPath + "'", HttpStatusCode.NotFound, null, null);
            if (info.LockState == LockState.Unlocked)
                return null;
            var inProgress = info.LocksInProgress?.FirstOrDefault();
            return new LockInfo
            {
                ModelPath = info.Path,
                LockState = info.LockState,
                UserName = inProgress?.UserName,
                TimeStamp = inProgress?.TimeStamp
            };
        }

        // Checks if the model (or folder) is locked. Server and transport errors are thrown, not turned into false.
        public static async Task<bool> IsModelLockedAsync(this RevitServerApi api, string modelPath)
        {
            return await GetModelLockAsync(api, modelPath).ConfigureAwait(false) != null;
        }

        // Gets the user of a lock in progress on the model; null when the model is not locked or the server does not report the owner.
        public static async Task<string> GetModelLockUserAsync(this RevitServerApi api, string modelPath)
        {
            var lockInfo = await GetModelLockAsync(api, modelPath).ConfigureAwait(false);
            return lockInfo?.UserName;
        }

        // The Revit Server REST service does not report who owns a lock, so this cannot be answered.
        public static Task<List<LockInfo>> GetLocksByUserAsync(this RevitServerApi api, string userName)
        {
            return Task.FromException<List<LockInfo>>(
                new NotSupportedException("The Revit Server REST service does not report the owner of a lock."));
        }

        // Gets the number of locked models and folders
        public static async Task<int> GetActiveLocksCountAsync(this RevitServerApi api)
        {
            var locks = await GetLocksAsync(api).ConfigureAwait(false);
            return locks.Locks.Count;
        }

        // Gets the last version comment
        public static async Task<string> GetLastVersionCommentAsync(this RevitServerApi api, string modelPath)
        {
            var latestVersion = await GetLatestVersionAsync(api, modelPath).ConfigureAwait(false);
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
            var history = await GetModelHistoryAsync(api, modelPath).ConfigureAwait(false);
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
                // Bounds of Unspecified kind are local time (as DateTime.ToUniversalTime treats them).
                var from = fromDate?.ToUniversalTime();
                var to = toDate?.ToUniversalTime();
                query = query.Where(h =>
                {
                    var date = h.DateUtc;
                    if (date == null) return false;
                    if (from.HasValue && date.Value < from.Value) return false;
                    if (to.HasValue && date.Value > to.Value) return false;
                    return true;
                });
            }

            // Order by version descending by default for paging the latest entries first
            query = query.OrderByDescending(h => h.Version);

            if (skip.HasValue)
            {
                query = query.Skip(Math.Max(skip.Value, 0));
            }
            if (take.HasValue)
            {
                query = query.Take(Math.Max(take.Value, 0));
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
            var history = await GetModelHistoryAsync(api, modelPath).ConfigureAwait(false);
            return history?.Items?.OrderByDescending(h => h.Version).Take(Math.Max(count, 0)).ToList() ?? new List<HistoryItem>();
        }

        // Gets a page of history (skip/take, client-side)
        public static async Task<List<HistoryItem>> GetModelHistoryPageAsync(this RevitServerApi api, string modelPath, int skip, int take)
        {
            var history = await GetModelHistoryAsync(api, modelPath).ConfigureAwait(false);
            return history?.Items?.OrderByDescending(h => h.Version).Skip(Math.Max(skip, 0)).Take(Math.Max(take, 0)).ToList() ?? new List<HistoryItem>();
        }

        // Gets all versions by user with optional limiting (client-side)
        public static async Task<List<HistoryItem>> GetVersionsByUserAsync(this RevitServerApi api, string modelPath, string userName, int? take)
        {
            var history = await GetModelHistoryAsync(api, modelPath).ConfigureAwait(false);
            var query = history?.Items?.Where(h => h.User?.Equals(userName, StringComparison.OrdinalIgnoreCase) == true)
                        .OrderByDescending(h => h.Version) ?? Enumerable.Empty<HistoryItem>();
            if (take.HasValue)
                query = query.Take(Math.Max(take.Value, 0));
            return query.ToList();
        }

        [DataContract]
        private class DescendentLocks
        {
            [DataMember(Name = "Items")] public List<string> Items { get; set; }
        }

        private static T DeserializeJson<T>(string json) where T : class
        {
            if (string.IsNullOrEmpty(json))
                return null;
            return JsonConvert.DeserializeObject<T>(json);
        }
    }
} 