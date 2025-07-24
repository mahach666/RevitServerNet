using System.Threading.Tasks;
using RevitServerNet.Models;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace RevitServerNet.Extensions
{
    /// <summary>
    /// Расширения для работы с историей файлов и блокировками
    /// </summary>
    public static class HistoryExtensions
    {
        /// <summary>
        /// Получает историю модели
        /// </summary>
        /// <param name="api">Экземпляр RevitServerApi</param>
        /// <param name="modelPath">Путь к модели</param>
        /// <returns>История модели</returns>
        public static async Task<ModelHistory> GetModelHistoryAsync(this RevitServerApi api, string modelPath)
        {
            var encodedPath = RevitServerApi.EncodePath(modelPath);
            var command = $"{encodedPath}/history";
            var json = await api.GetAsync(command);
            return DeserializeJson<ModelHistory>(json);
        }

        /// <summary>
        /// Получает последнюю версию модели
        /// </summary>
        /// <param name="api">Экземпляр RevitServerApi</param>
        /// <param name="modelPath">Путь к модели</param>
        /// <returns>Информация о последней версии</returns>
        public static async Task<HistoryItem> GetLatestVersionAsync(this RevitServerApi api, string modelPath)
        {
            var history = await GetModelHistoryAsync(api, modelPath);
            return history?.Items?.OrderByDescending(h => h.Version).FirstOrDefault();
        }

        /// <summary>
        /// Получает конкретную версию модели
        /// </summary>
        /// <param name="api">Экземпляр RevitServerApi</param>
        /// <param name="modelPath">Путь к модели</param>
        /// <param name="version">Номер версии</param>
        /// <returns>Информация о версии</returns>
        public static async Task<HistoryItem> GetVersionAsync(this RevitServerApi api, string modelPath, int version)
        {
            var history = await GetModelHistoryAsync(api, modelPath);
            return history?.Items?.FirstOrDefault(h => h.Version == version);
        }

        /// <summary>
        /// Получает список версий модели по пользователю
        /// </summary>
        /// <param name="api">Экземпляр RevitServerApi</param>
        /// <param name="modelPath">Путь к модели</param>
        /// <param name="userName">Имя пользователя</param>
        /// <returns>Список версий пользователя</returns>
        public static async Task<List<HistoryItem>> GetVersionsByUserAsync(this RevitServerApi api, string modelPath, string userName)
        {
            var history = await GetModelHistoryAsync(api, modelPath);
            return history?.Items?.Where(h => h.User?.Equals(userName, System.StringComparison.OrdinalIgnoreCase) == true).ToList() 
                   ?? new List<HistoryItem>();
        }

        /// <summary>
        /// Получает количество версий модели
        /// </summary>
        /// <param name="api">Экземпляр RevitServerApi</param>
        /// <param name="modelPath">Путь к модели</param>
        /// <returns>Количество версий</returns>
        public static async Task<int> GetVersionCountAsync(this RevitServerApi api, string modelPath)
        {
            var history = await GetModelHistoryAsync(api, modelPath);
            return history?.Items?.Count ?? 0;
        }

        /// <summary>
        /// Получает общий размер всех версий модели
        /// </summary>
        /// <param name="api">Экземпляр RevitServerApi</param>
        /// <param name="modelPath">Путь к модели</param>
        /// <returns>Общий размер в байтах</returns>
        public static async Task<long> GetTotalModelSizeAsync(this RevitServerApi api, string modelPath)
        {
            var history = await GetModelHistoryAsync(api, modelPath);
            return history?.Items?.Sum(h => h.Size + h.SupportSize) ?? 0;
        }

        /// <summary>
        /// Получает список блокировок сервера
        /// </summary>
        /// <param name="api">Экземпляр RevitServerApi</param>
        /// <returns>Список блокировок</returns>
        public static async Task<LocksList> GetLocksAsync(this RevitServerApi api)
        {
            var json = await api.GetAsync("locks");
            return DeserializeJson<LocksList>(json);
        }

        /// <summary>
        /// Получает блокировки конкретной модели
        /// </summary>
        /// <param name="api">Экземпляр RevitServerApi</param>
        /// <param name="modelPath">Путь к модели</param>
        /// <returns>Информация о блокировке модели</returns>
        public static async Task<LockInfo> GetModelLockAsync(this RevitServerApi api, string modelPath)
        {
            var encodedPath = RevitServerApi.EncodePath(modelPath);
            var command = $"{encodedPath}/lock";
            var json = await api.GetAsync(command);
            return DeserializeJson<LockInfo>(json);
        }

        /// <summary>
        /// Проверяет заблокирована ли модель
        /// </summary>
        /// <param name="api">Экземпляр RevitServerApi</param>
        /// <param name="modelPath">Путь к модели</param>
        /// <returns>True если модель заблокирована</returns>
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

        /// <summary>
        /// Получает информацию о пользователе, заблокировавшем модель
        /// </summary>
        /// <param name="api">Экземпляр RevitServerApi</param>
        /// <param name="modelPath">Путь к модели</param>
        /// <returns>Имя пользователя или null</returns>
        public static async Task<string> GetModelLockUserAsync(this RevitServerApi api, string modelPath)
        {
            var lockInfo = await GetModelLockAsync(api, modelPath);
            return lockInfo?.UserName;
        }

        /// <summary>
        /// Получает список блокировок по пользователю
        /// </summary>
        /// <param name="api">Экземпляр RevitServerApi</param>
        /// <param name="userName">Имя пользователя</param>
        /// <returns>Список блокировок пользователя</returns>
        public static async Task<List<LockInfo>> GetLocksByUserAsync(this RevitServerApi api, string userName)
        {
            var allLocks = await GetLocksAsync(api);
            return allLocks?.Locks?.Where(l => l.UserName?.Equals(userName, System.StringComparison.OrdinalIgnoreCase) == true).ToList()
                   ?? new List<LockInfo>();
        }

        /// <summary>
        /// Получает количество активных блокировок
        /// </summary>
        /// <param name="api">Экземпляр RevitServerApi</param>
        /// <returns>Количество блокировок</returns>
        public static async Task<int> GetActiveLocksCountAsync(this RevitServerApi api)
        {
            var locks = await GetLocksAsync(api);
            return locks?.Locks?.Count ?? 0;
        }

        /// <summary>
        /// Получает список уникальных пользователей из истории
        /// </summary>
        /// <param name="api">Экземпляр RevitServerApi</param>
        /// <param name="modelPath">Путь к модели</param>
        /// <returns>Список пользователей</returns>
        public static async Task<List<string>> GetModelContributorsAsync(this RevitServerApi api, string modelPath)
        {
            var history = await GetModelHistoryAsync(api, modelPath);
            return history?.Items?.Where(h => !string.IsNullOrEmpty(h.User))
                                  .Select(h => h.User)
                                  .Distinct(System.StringComparer.OrdinalIgnoreCase)
                                  .ToList()
                   ?? new List<string>();
        }

        /// <summary>
        /// Получает последнюю дату изменения модели
        /// </summary>
        /// <param name="api">Экземпляр RevitServerApi</param>
        /// <param name="modelPath">Путь к модели</param>
        /// <returns>Дата последнего изменения</returns>
        public static async Task<string> GetLastModifiedDateAsync(this RevitServerApi api, string modelPath)
        {
            var latestVersion = await GetLatestVersionAsync(api, modelPath);
            return latestVersion?.Date;
        }

        /// <summary>
        /// Получает комментарий к последней версии
        /// </summary>
        /// <param name="api">Экземпляр RevitServerApi</param>
        /// <param name="modelPath">Путь к модели</param>
        /// <returns>Комментарий к последней версии</returns>
        public static async Task<string> GetLastVersionCommentAsync(this RevitServerApi api, string modelPath)
        {
            var latestVersion = await GetLatestVersionAsync(api, modelPath);
            return latestVersion?.Comment;
        }

        /// <summary>
        /// Десериализует JSON в объект
        /// </summary>
        /// <typeparam name="T">Тип объекта</typeparam>
        /// <param name="json">JSON строка</param>
        /// <returns>Десериализованный объект</returns>
        private static T DeserializeJson<T>(string json) where T : class
        {
            if (string.IsNullOrEmpty(json))
                return null;
            return JsonConvert.DeserializeObject<T>(json);
        }
    }
} 