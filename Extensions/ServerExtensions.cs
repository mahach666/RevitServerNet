using Newtonsoft.Json;
using RevitServerNet.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RevitServerNet.Extensions
{
    // Extensions for working with server properties and status
    public static class ServerExtensions
    {
        // Gets server info
        public static async Task<ServerInfo> GetServerInfoAsync(this RevitServerApi api)
        {
            var json = await api.GetAsync("serverproperties");
            return DeserializeJson<ServerInfo>(json);
        }

        // Checks server availability by getting server info
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

        // Gets basic server status info (from serverProperties)
        public static async Task<bool> IsServerRunningAsync(this RevitServerApi api)
        {
            try
            {
                var serverInfo = await GetServerInfoAsync(api);
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

        // Gets server version
        public static async Task<string> GetServerVersionAsync(this RevitServerApi api)
        {
            var serverInfo = await GetServerInfoAsync(api);
            return serverInfo?.Version;
        }

        // Gets server roles
        public static async Task<List<ServerRole>> GetServerRolesAsync(this RevitServerApi api)
        {
            var serverInfo = await GetServerInfoAsync(api);
            return serverInfo?.Roles;
        }

        public static async Task<int> GetMaximumModelNameLengthAsync(this RevitServerApi api)
        {
            var serverInfo = await GetServerInfoAsync(api);
            return serverInfo?.MaxNameLength ?? 0;
        }

        // Gets server drive info
        public static async Task<(long DriveSpace, long DriveFreeSpace)> GetServerDriveInfoAsync(this RevitServerApi api)
        {
            var contents = await api.GetRootFolderContentsAsync(); 
            return (contents?.DriveSpace ?? 0, contents?.DriveFreeSpace ?? 0);
        }

        private static T DeserializeJson<T>(string json) where T : class
        {
            if (string.IsNullOrEmpty(json))
                return null;
            return JsonConvert.DeserializeObject<T>(json);
        }
    }
} 