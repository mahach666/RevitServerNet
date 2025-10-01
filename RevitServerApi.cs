using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace RevitServerNet
{
    /// <summary>
    /// Main class for working with Revit Server REST API
    /// </summary>
    public class RevitServerApi
    {
        private readonly string _baseUrl;
        private readonly string _userName;
        
        // Official API paths for different versions (from Autodesk sample)
        private static readonly Dictionary<string, string> SupportedVersions = new Dictionary<string, string>
        {
            {"2012", "/RevitServerAdminRESTService/AdminRESTService.svc"},
            {"2013", "/RevitServerAdminRESTService2013/AdminRESTService.svc"},
            {"2014", "/RevitServerAdminRESTService2014/AdminRESTService.svc"},
            {"2015", "/RevitServerAdminRESTService2015/AdminRESTService.svc"},
            {"2016", "/RevitServerAdminRESTService2016/AdminRESTService.svc"},
            {"2017", "/RevitServerAdminRESTService2017/AdminRESTService.svc"},
            {"2018", "/RevitServerAdminRESTService2018/AdminRESTService.svc"},
            {"2019", "/RevitServerAdminRESTService2019/AdminRESTService.svc"},
            {"2020", "/RevitServerAdminRESTService2020/AdminRESTService.svc"},
            {"2021", "/RevitServerAdminRESTService2021/AdminRESTService.svc"},
            {"2022", "/RevitServerAdminRESTService2022/AdminRESTService.svc"},
            {"2023", "/RevitServerAdminRESTService2023/AdminRESTService.svc"},
            {"2024", "/RevitServerAdminRESTService2024/AdminRESTService.svc"},
            {"2025", "/RevitServerAdminRESTService2025/AdminRESTService.svc"},
            {"2026", "/RevitServerAdminRESTService2026/AdminRESTService.svc"},
        };

        /// <summary>
        /// Initializes a new instance of RevitServerApi
        /// </summary>
        /// <param name="host">Revit Server host (e.g. "localhost" or IP address)</param>
        /// <param name="userName">User name for API requests</param>
        /// <param name="useHttps">Use HTTPS (default: false)</param>
        /// <param name="serverVersion">Server version (default: "2019")</param>
        public RevitServerApi(string host, string userName, bool useHttps = false, string serverVersion = "2019")
        {
            if (string.IsNullOrEmpty(host))
                throw new ArgumentException("Host cannot be null or empty", nameof(host));
            if (string.IsNullOrEmpty(userName))
                throw new ArgumentException("UserName cannot be null or empty", nameof(userName));
            
            if (!SupportedVersions.ContainsKey(serverVersion))
                throw new ArgumentException($"Unsupported server version '{serverVersion}'. Supported versions: {string.Join(", ", SupportedVersions.Keys)}", nameof(serverVersion));

            var protocol = useHttps ? "https" : "http";
            var servicePath = SupportedVersions[serverVersion];
            _baseUrl = $"{protocol}://{host}{servicePath}";
            _userName = userName;
        }

        /// <summary>
        /// Base URL for API
        /// </summary>
        public string BaseUrl => _baseUrl;

        /// <summary>
        /// User name
        /// </summary>
        public string UserName => _userName;

        /// <summary>
        /// Performs GET request to API
        /// </summary>
        /// <param name="command">API command</param>
        /// <param name="additionalHeaders">Additional headers</param>
        /// <returns>JSON response from server</returns>
        public async Task<string> GetAsync(string command, Dictionary<string, string> additionalHeaders = null)
        {
            return await ExecuteRequestAsync("GET", command, additionalHeaders);
        }

        /// <summary>
        /// Performs POST request to API
        /// </summary>
        /// <param name="command">API command</param>
        /// <param name="data">Data to send</param>
        /// <param name="additionalHeaders">Additional headers</param>
        /// <returns>JSON response from server</returns>
        public async Task<string> PostAsync(string command, string data = null, Dictionary<string, string> additionalHeaders = null)
        {
            return await ExecuteRequestAsync("POST", command, additionalHeaders, data);
        }

        /// <summary>
        /// Performs PUT request to API
        /// </summary>
        /// <param name="command">API command</param>
        /// <param name="data">Data to send</param>
        /// <param name="additionalHeaders">Additional headers</param>
        /// <returns>JSON response from server</returns>
        public async Task<string> PutAsync(string command, string data = null, Dictionary<string, string> additionalHeaders = null)
        {
            return await ExecuteRequestAsync("PUT", command, additionalHeaders, data);
        }

        /// <summary>
        /// Performs DELETE request to API
        /// </summary>
        /// <param name="command">API command</param>
        /// <param name="additionalHeaders">Additional headers</param>
        /// <returns>JSON response from server</returns>
        public async Task<string> DeleteAsync(string command, Dictionary<string, string> additionalHeaders = null)
        {
            return await ExecuteRequestAsync("DELETE", command, additionalHeaders);
        }

        /// <summary>
        /// Encodes path in API format (replaces separators with |)
        /// </summary>
        /// <param name="path">Path to file or folder</param>
        /// <returns>Encoded path</returns>
        public static string EncodePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return "|"; // Root folder

            // Replace different separators with |
            path = path.Replace('\\', '|').Replace('/', '|');
            
            // Remove double separators
            while (path.Contains("||"))
                path = path.Replace("||", "|");

            // Add | at the beginning if it's not there
            if (!path.StartsWith("|"))
                path = "|" + path;

            return path;
        }

        /// <summary>
        /// Performs HTTP request to API
        /// </summary>
        /// <param name="method">HTTP method</param>
        /// <param name="command">API command</param>
        /// <param name="additionalHeaders">Additional headers</param>
        /// <param name="data">Data to send</param>
        /// <returns>JSON response from server</returns>
        private async Task<string> ExecuteRequestAsync(string method, string command, Dictionary<string, string> additionalHeaders = null, string data = null)
        {
            var url = $"{_baseUrl}/{command}";
            var request = WebRequest.Create(url) as HttpWebRequest;
            // Remove debug logging for release
            if (request == null)
                throw new InvalidOperationException("Cannot create HTTP request");

            request.Method = method;
            // Enable transparent decompression to reduce payload sizes (if server supports it)
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            // Set ContentType only for POST/PUT with data
            if (method == "POST" || method == "PUT")
            {
                if (!string.IsNullOrEmpty(data))
                {
                    request.ContentType = "application/json";
                }
                else
                {
                    // Some RS endpoints require explicit Content-Length: 0
                    request.ContentLength = 0;
                }
            }
            // Add required headers for Revit Server API
            var machineName = Environment.MachineName;
            var operationGuid = Guid.NewGuid().ToString();
            if (string.IsNullOrEmpty(_userName))
                throw new InvalidOperationException("User name cannot be null or empty");
            if (string.IsNullOrEmpty(machineName))
                throw new InvalidOperationException("Machine name cannot be null or empty");
            request.Headers.Add("User-Name", _userName);
            request.Headers.Add("User-Machine-Name", machineName);
            request.Headers.Add("Operation-GUID", operationGuid);
            // Add additional headers if provided
            if (additionalHeaders != null)
            {
                foreach (var header in additionalHeaders)
                {
                    request.Headers.Add(header.Key, header.Value);
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
                        throw new RevitServerApiException($"API request failed with status {errorResponse.StatusCode}: {errorContent}", ex);
                    }
                }
                throw new RevitServerApiException("API request failed", ex);
            }
        }
    }

    /// <summary>
    /// Exception for Revit Server API errors
    /// </summary>
    public class RevitServerApiException : Exception
    {
        public RevitServerApiException(string message) : base(message) { }
        public RevitServerApiException(string message, Exception innerException) : base(message, innerException) { }
    }
}
