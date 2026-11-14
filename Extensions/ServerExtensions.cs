using Newtonsoft.Json;
using RevitServerNet.Models;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace RevitServerNet.Extensions
{
    // Extensions for working with server properties and status
    public static class ServerExtensions
    {
        private static readonly Regex YearInBaseUrl = new Regex(@"/RevitServerAdminRESTService(\d{4})?/", RegexOptions.CultureInvariant);

        // Gets server info. The server does not return a version: ServerInfo.Version is the year of the server
        // version the client was created with (the year in api.BaseUrl; "2012" for the unversioned 2012 service path).
        public static async Task<ServerInfo> GetServerInfoAsync(this RevitServerApi api)
        {
            var json = await api.GetAsync("serverproperties").ConfigureAwait(false);
            var info = DeserializeJson<ServerInfo>(json);
            if (info != null)
            {
                var m = YearInBaseUrl.Match(api.BaseUrl ?? string.Empty);
                if (m.Success)
                    info.Version = m.Groups[1].Success ? m.Groups[1].Value : "2012";
            }
            return info;
        }

        // Checks server availability by getting server info
        public static async Task<bool> PingServerAsync(this RevitServerApi api)
        {
            try
            {
                var serverInfo = await GetServerInfoAsync(api).ConfigureAwait(false);
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
                var serverInfo = await GetServerInfoAsync(api).ConfigureAwait(false);
                // Version is derived from the client, so it says nothing about the server answering.
                return serverInfo != null &&
                    (!string.IsNullOrEmpty(serverInfo.Name) ||
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

        // Gets the server version: the year of the version the client was created with (the server does not report it),
        // after checking that the server answers.
        public static async Task<string> GetServerVersionAsync(this RevitServerApi api)
        {
            var serverInfo = await GetServerInfoAsync(api).ConfigureAwait(false);
            return serverInfo?.Version;
        }

        // Gets server roles
        public static async Task<List<ServerRole>> GetServerRolesAsync(this RevitServerApi api)
        {
            var serverInfo = await GetServerInfoAsync(api).ConfigureAwait(false);
            return serverInfo?.Roles;
        }

        public static async Task<int> GetMaximumModelNameLengthAsync(this RevitServerApi api)
        {
            var serverInfo = await GetServerInfoAsync(api).ConfigureAwait(false);
            return serverInfo?.MaxNameLength ?? 0;
        }

        // Gets server drive info
        public static async Task<(long DriveSpace, long DriveFreeSpace)> GetServerDriveInfoAsync(this RevitServerApi api)
        {
            var contents = await api.GetRootFolderContentsAsync().ConfigureAwait(false);
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