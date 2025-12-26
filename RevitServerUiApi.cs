using Newtonsoft.Json;
using RevitServerNet.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace RevitServerNet
{
    /// <summary>
    /// Client for Revit Server Admin UI API (used by RevitServerAdmin{YEAR} web UI).
    /// Base path example: http://{host}/RevitServerAdmin2022/api/...
    /// </summary>
    public class RevitServerUiApi
    {
        private readonly string _baseUrl;
        private readonly CookieContainer _cookies = new CookieContainer();

        // Admin UI virtual directory naming:
        // - version <= 2012: /RevitServerAdmin
        // - version > 2012:  /RevitServerAdmin{YEAR}
        private static string GetAdminUiVirtualDirectory(string serverVersion)
        {
            if (string.IsNullOrWhiteSpace(serverVersion))
                throw new ArgumentException("Server version cannot be null or empty", nameof(serverVersion));

            if (!int.TryParse(serverVersion, out var year))
                throw new ArgumentException($"Server version '{serverVersion}' is not a valid year.", nameof(serverVersion));

            // Library historically targets RS 2012+.
            if (year < 2012)
                throw new ArgumentException("Server version must be >= 2012.", nameof(serverVersion));

            return year <= 2012 ? "RevitServerAdmin" : $"RevitServerAdmin{year}";
        }

        /// <summary>
        /// Initializes a new instance of RevitServerUiApi.
        /// </summary>
        /// <param name="host">Revit Server host (e.g. "localhost" or IP address)</param>
        /// <param name="useHttps">Use HTTPS (default: false)</param>
        /// <param name="serverVersion">Server year (default: "2022")</param>
        public RevitServerUiApi(string host, bool useHttps = false, string serverVersion = "2022")
        {
            if (string.IsNullOrWhiteSpace(host))
                throw new ArgumentException("Host cannot be null or empty", nameof(host));

            var protocol = useHttps ? "https" : "http";
            var vdir = GetAdminUiVirtualDirectory(serverVersion);
            _baseUrl = $"{protocol}://{host}/{vdir}";
        }

        /// <summary>
        /// Base URL for UI API (without /api suffix).
        /// </summary>
        public string BaseUrl => _baseUrl;

        // ---------------------------
        // Raw HTTP helpers
        // ---------------------------

        public Task<string> GetAsync(string apiPath, Dictionary<string, string> query = null)
            => ExecuteRequestAsync("GET", apiPath, query);

        public Task<string> PostAsync(string apiPath, Dictionary<string, string> query = null, string data = null)
            => ExecuteRequestAsync("POST", apiPath, query, data);

        public Task<string> PutAsync(string apiPath, Dictionary<string, string> query = null, string data = null)
            => ExecuteRequestAsync("PUT", apiPath, query, data);

        public Task<string> DeleteAsync(string apiPath, Dictionary<string, string> query = null)
            => ExecuteRequestAsync("DELETE", apiPath, query);

        private async Task<string> ExecuteRequestAsync(string method, string apiPath, Dictionary<string, string> query = null, string data = null)
        {
            if (string.IsNullOrWhiteSpace(apiPath))
                throw new ArgumentException("API path cannot be null or empty", nameof(apiPath));

            apiPath = apiPath.TrimStart('/');
            var url = $"{_baseUrl}/{apiPath}";
            if (query != null && query.Count > 0)
            {
                url += "?" + BuildQuery(query);
            }

            var request = WebRequest.Create(url) as HttpWebRequest;
            if (request == null)
                throw new InvalidOperationException("Cannot create HTTP request");

            request.Method = method;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            request.CookieContainer = _cookies;
            request.UseDefaultCredentials = true;
            request.Credentials = CredentialCache.DefaultCredentials;
            request.Accept = "application/json";

            // UI endpoints often work without body; still some require explicit Content-Length: 0.
            if (method == "POST" || method == "PUT")
            {
                if (!string.IsNullOrEmpty(data))
                {
                    request.ContentType = "application/json";
                }
                else
                {
                    request.ContentLength = 0;
                }
            }

            if (!string.IsNullOrEmpty(data) && (method == "POST" || method == "PUT"))
            {
                var bytes = Encoding.UTF8.GetBytes(data);
                request.ContentLength = bytes.Length;
                using (var stream = await request.GetRequestStreamAsync())
                {
                    await stream.WriteAsync(bytes, 0, bytes.Length);
                }
            }

            try
            {
                using (var response = await request.GetResponseAsync() as HttpWebResponse)
                using (var stream = response?.GetResponseStream())
                {
                    if (stream == null)
                        return null;
                    using (var reader = new StreamReader(stream))
                    {
                        return await reader.ReadToEndAsync();
                    }
                }
            }
            catch (WebException ex)
            {
                if (ex.Response is HttpWebResponse errorResponse)
                {
                    using (var stream = errorResponse.GetResponseStream())
                    using (var reader = new StreamReader(stream))
                    {
                        var errorContent = await reader.ReadToEndAsync();
                        throw new RevitServerUiApiException($"UI API request failed with status {errorResponse.StatusCode}: {errorContent}", ex);
                    }
                }
                throw new RevitServerUiApiException("UI API request failed", ex);
            }
        }

        private static string BuildQuery(Dictionary<string, string> query)
        {
            var sb = new StringBuilder();
            var first = true;
            foreach (var kv in query)
            {
                if (kv.Key == null) continue;
                if (!first) sb.Append("&");
                first = false;
                sb.Append(Uri.EscapeDataString(kv.Key));
                sb.Append("=");
                sb.Append(Uri.EscapeDataString(kv.Value ?? string.Empty));
            }
            return sb.ToString();
        }

        // ---------------------------
        // Typed UI methods (from searchUIapi.txt)
        // ---------------------------

        /// <summary>
        /// GET /api/model/details?id={id}
        /// </summary>
        public async Task<UiModelDetails> GetModelDetailsAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("id cannot be null or empty", nameof(id));
            var json = await GetAsync("api/model/details", new Dictionary<string, string> { { "id", id } });
            return DeserializeJson<UiModelDetails>(json);
        }

        /// <summary>
        /// GET /api/folder/ItemLockData?id={id}&amp;placeholder=
        /// </summary>
        public async Task<UiItemLockData> GetItemLockDataAsync(string id, string placeholder = "")
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("id cannot be null or empty", nameof(id));
            var json = await GetAsync("api/folder/ItemLockData",
                new Dictionary<string, string>
                {
                    { "id", id },
                    { "placeholder", placeholder ?? string.Empty }
                });
            return DeserializeJson<UiItemLockData>(json);
        }

        /// <summary>
        /// GET /api/model/ModelHistories?type=rs-model&amp;id={id}
        /// </summary>
        public async Task<List<UiModelHistoryItem>> GetModelHistoriesAsync(string id, string type = "rs-model")
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("id cannot be null or empty", nameof(id));
            if (string.IsNullOrWhiteSpace(type)) throw new ArgumentException("type cannot be null or empty", nameof(type));
            var json = await GetAsync("api/model/ModelHistories",
                new Dictionary<string, string>
                {
                    { "type", type },
                    { "id", id }
                });
            return DeserializeJson<List<UiModelHistoryItem>>(json) ?? new List<UiModelHistoryItem>();
        }

        /// <summary>
        /// GET /api/folder/SubItems?id={id}&amp;depth={depth}
        /// </summary>
        public async Task<UiTreeItem> GetSubItemsAsync(string id, int depth = 1)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("id cannot be null or empty", nameof(id));
            if (depth < 0) throw new ArgumentOutOfRangeException(nameof(depth), "depth cannot be negative");
            var json = await GetAsync("api/folder/SubItems",
                new Dictionary<string, string>
                {
                    { "id", id },
                    { "depth", depth.ToString() }
                });
            return DeserializeJson<UiTreeItem>(json);
        }

        /// <summary>
        /// DELETE /api/operation?operationtype=DeleteOrRename&amp;id={id}[&amp;newName={newName}]
        /// newName omitted -> delete; provided -> rename
        /// </summary>
        public async Task DeleteOrRenameAsync(string id, string newName = null)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("id cannot be null or empty", nameof(id));
            var q = new Dictionary<string, string>
            {
                { "operationtype", "DeleteOrRename" },
                { "id", id }
            };
            if (!string.IsNullOrWhiteSpace(newName))
                q["newName"] = newName;
            await DeleteAsync("api/operation", q);
        }

        public enum PasteAction
        {
            Copy,
            Move
        }

        /// <summary>
        /// POST /api/folder/copyOrMove?id={id}&amp;destinationid={destinationId}&amp;pasteAction={Copy|Move}&amp;replaceExisting={true|false}
        /// Returns a URL (JSON string in response body).
        /// </summary>
        public async Task<string> CopyOrMoveAsync(string id, string destinationId, PasteAction pasteAction, bool replaceExisting = false)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("id cannot be null or empty", nameof(id));
            if (string.IsNullOrWhiteSpace(destinationId)) throw new ArgumentException("destinationId cannot be null or empty", nameof(destinationId));

            var json = await PostAsync("api/folder/copyOrMove",
                new Dictionary<string, string>
                {
                    { "id", id },
                    { "destinationid", destinationId },
                    { "pasteAction", pasteAction.ToString() },
                    { "replaceExisting", replaceExisting ? "true" : "false" }
                });

            return DeserializeJsonStringOrRaw(json);
        }

        /// <summary>
        /// PUT /api/operation?operationType=Lock&amp;id={id}
        /// </summary>
        public async Task LockAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("id cannot be null or empty", nameof(id));
            await PutAsync("api/operation",
                new Dictionary<string, string>
                {
                    { "operationType", "Lock" },
                    { "id", id }
                });
        }

        /// <summary>
        /// DELETE /api/operation?operationtype=Unlock&amp;id={id}&amp;itemMustExist={true|false}
        /// </summary>
        public async Task UnlockAsync(string id, bool itemMustExist = true)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("id cannot be null or empty", nameof(id));
            await DeleteAsync("api/operation",
                new Dictionary<string, string>
                {
                    { "operationtype", "Unlock" },
                    { "id", id },
                    { "itemMustExist", itemMustExist ? "true" : "false" }
                });
        }

        /// <summary>
        /// PUT /api/folder/createfolder?id={id}
        /// Returns a URL (JSON string in response body).
        /// </summary>
        public async Task<string> CreateFolderAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("id cannot be null or empty", nameof(id));
            var json = await PutAsync("api/folder/createfolder",
                new Dictionary<string, string>
                {
                    { "id", id }
                });
            return DeserializeJsonStringOrRaw(json);
        }

        private static string DeserializeJsonStringOrRaw(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return json;
            try
            {
                // Many UI endpoints return a JSON string like: "http://host/server/path"
                return JsonConvert.DeserializeObject<string>(json);
            }
            catch
            {
                return json.Trim();
            }
        }

        private static T DeserializeJson<T>(string json) where T : class
        {
            if (string.IsNullOrEmpty(json))
                return null;
            return JsonConvert.DeserializeObject<T>(json);
        }
    }

    /// <summary>
    /// Exception for Revit Server UI API errors.
    /// </summary>
    public class RevitServerUiApiException : Exception
    {
        public RevitServerUiApiException(string message) : base(message) { }
        public RevitServerUiApiException(string message, Exception innerException) : base(message, innerException) { }
    }
}


