using System.Threading.Tasks;
using RevitServerNet.Models;
using Newtonsoft.Json;

namespace RevitServerNet.Extensions
{
    /// <summary>
    /// Расширения для работы с серверными свойствами и статусом
    /// </summary>
    public static class ServerExtensions
    {
        /// <summary>
        /// Получает информацию о сервере
        /// </summary>
        /// <param name="api">Экземпляр RevitServerApi</param>
        /// <returns>Информация о сервере</returns>
        public static async Task<ServerInfo> GetServerInfoAsync(this RevitServerApi api)
        {
            var json = await api.GetAsync("serverproperties"); // ИСПРАВЛЕНО: нижний регистр как в Python коде
            return DeserializeJson<ServerInfo>(json);
        }

        /// <summary>
        /// Проверяет доступность сервера через получение информации о сервере
        /// </summary>
        /// <param name="api">Экземпляр RevitServerApi</param>
        /// <returns>True если сервер доступен</returns>
        public static async Task<bool> PingServerAsync(this RevitServerApi api)
        {
            try
            {
                var serverInfo = await GetServerInfoAsync(api);
                return serverInfo != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Получает базовую информацию о статусе сервера (на основе serverProperties)
        /// </summary>
        /// <param name="api">Экземпляр RevitServerApi</param>
        /// <returns>Информация о статусе</returns>
        public static async Task<bool> IsServerRunningAsync(this RevitServerApi api)
        {
            try
            {
                var serverInfo = await GetServerInfoAsync(api);
                // Проверяем наличие хотя бы одного ключевого поля
                return serverInfo != null &&
                    (!string.IsNullOrEmpty(serverInfo.Name) ||
                     !string.IsNullOrEmpty(serverInfo.Version) ||
                     !string.IsNullOrEmpty(serverInfo.MachineName) ||
                     (serverInfo.Roles != null && serverInfo.Roles.Count > 0) ||
                     serverInfo.MaxPathLength > 0 ||
                     serverInfo.MaxNameLength > 0);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Получает версию сервера
        /// </summary>
        /// <param name="api">Экземпляр RevitServerApi</param>
        /// <returns>Версия сервера</returns>
        public static async Task<string> GetServerVersionAsync(this RevitServerApi api)
        {
            var serverInfo = await GetServerInfoAsync(api);
            return serverInfo?.ServerVersion;
        }

        /// <summary>
        /// Получает список ролей сервера
        /// </summary>
        /// <param name="api">Экземпляр RevitServerApi</param>
        /// <returns>Роли сервера</returns>
        public static async Task<System.Collections.Generic.List<string>> GetServerRolesAsync(this RevitServerApi api)
        {
            var serverInfo = await GetServerInfoAsync(api);
            return serverInfo?.ServerRoles ?? new System.Collections.Generic.List<string>();
        }

        /// <summary>
        /// Получает корневой путь сервера
        /// </summary>
        /// <param name="api">Экземпляр RevitServerApi</param>
        /// <returns>Корневой путь</returns>
        public static async Task<string> GetRootPathAsync(this RevitServerApi api)
        {
            var serverInfo = await GetServerInfoAsync(api);
            return serverInfo?.RootPath;
        }

        /// <summary>
        /// Получает максимальную длину пути папки
        /// </summary>
        /// <param name="api">Экземпляр RevitServerApi</param>
        /// <returns>Максимальная длина пути папки</returns>
        public static async Task<int> GetMaximumFolderPathLengthAsync(this RevitServerApi api)
        {
            var serverInfo = await GetServerInfoAsync(api);
            return serverInfo?.MaximumFolderPathLength ?? 0;
        }

        /// <summary>
        /// Получает максимальную длину имени модели
        /// </summary>
        /// <param name="api">Экземпляр RevitServerApi</param>
        /// <returns>Максимальная длина имени модели</returns>
        public static async Task<int> GetMaximumModelNameLengthAsync(this RevitServerApi api)
        {
            var serverInfo = await GetServerInfoAsync(api);
            return serverInfo?.MaximumModelNameLength ?? 0;
        }

        /// <summary>
        /// Получает информацию о дисковом пространстве сервера (как в Python getdriveinfo)
        /// </summary>
        /// <param name="api">Экземпляр RevitServerApi</param>
        /// <returns>Информация о свободном и общем месте на диске</returns>
        public static async Task<(long DriveSpace, long DriveFreeSpace)> GetServerDriveInfoAsync(this RevitServerApi api)
        {
            var contents = await api.GetRootFolderContentsAsync(); 
            return (contents?.TotalSpace ?? 0, contents?.FreeSpace ?? 0); // ИСПРАВЛЕНО: правильные названия свойств
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