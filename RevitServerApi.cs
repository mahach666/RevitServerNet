using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
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

        // Official Admin REST virtual directory naming:
        // - version <= 2012: /RevitServerAdminRESTService/AdminRESTService.svc
        // - version > 2012:  /RevitServerAdminRESTService{YEAR}/AdminRESTService.svc
        private static string GetAdminRestServicePath(string serverVersion)
        {
            if (string.IsNullOrWhiteSpace(serverVersion))
                throw new ArgumentException("Server version cannot be null or empty", nameof(serverVersion));

            if (!int.TryParse(serverVersion, out var year))
                throw new ArgumentException($"Server version '{serverVersion}' is not a valid year.", nameof(serverVersion));

            // Library historically targets RS 2012+.
            if (year < 2012)
                throw new ArgumentException("Server version must be >= 2012.", nameof(serverVersion));

            var dir = year <= 2012
                ? "RevitServerAdminRESTService"
                : $"RevitServerAdminRESTService{year}";

            return $"/{dir}/AdminRESTService.svc";
        }

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

            var protocol = useHttps ? "https" : "http";
            var servicePath = GetAdminRestServicePath(serverVersion);
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

        private TimeSpan? _timeout;

        /// <summary>
        /// Time limit of one REST request, from sending it to reading the whole response. When it elapses the request is aborted
        /// and a <see cref="RevitServerApiException"/> is thrown; <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> means no limit.
        /// null (the default) keeps the behaviour of the platform, as before this property existed: on .NET 6/8 HttpWebRequest
        /// ends a request after 100 s, on .NET Framework its async calls have no limit.
        /// </summary>
        public TimeSpan? Timeout
        {
            get => _timeout;
            set
            {
                // CancellationTokenSource.CancelAfter accepts at most int.MaxValue milliseconds (about 24.8 days) on .NET Framework
                if (value.HasValue && value.Value != System.Threading.Timeout.InfiniteTimeSpan
                    && (value.Value <= TimeSpan.Zero || value.Value.TotalMilliseconds > int.MaxValue))
                    throw new ArgumentOutOfRangeException(nameof(value), "Timeout must be positive and at most int.MaxValue milliseconds, Timeout.InfiniteTimeSpan or null.");
                _timeout = value;
            }
        }

        // The server accepts only ASCII in header values (raw UTF-8 gets 400 "Missing or invalid User-Name"; HttpWebRequest throws
        // on non-ASCII on .NET 8 and sends Latin-1 on .NET Framework). Anything else is sent percent-encoded, which the server accepts.
        private static string EncodeHeaderValue(string value)
        {
            if (value == null)
                return null;
            foreach (var c in value)
                if (c < 0x20 || c > 0x7E)
                    return Uri.EscapeDataString(value);
            return value;
        }

        /// <summary>
        /// Performs GET request to API
        /// </summary>
        /// <param name="command">API command</param>
        /// <param name="additionalHeaders">Additional headers</param>
        /// <returns>JSON response from server</returns>
        public Task<string> GetAsync(string command, Dictionary<string, string> additionalHeaders = null)
            => ExecuteRequestAsync("GET", command, additionalHeaders, null, CancellationToken.None);

        /// <summary>
        /// Performs GET request to API; the request is aborted when <paramref name="cancellationToken"/> is cancelled
        /// </summary>
        public Task<string> GetAsync(string command, Dictionary<string, string> additionalHeaders, CancellationToken cancellationToken)
            => ExecuteRequestAsync("GET", command, additionalHeaders, null, cancellationToken);

        /// <summary>
        /// Performs POST request to API
        /// </summary>
        /// <param name="command">API command</param>
        /// <param name="data">Data to send</param>
        /// <param name="additionalHeaders">Additional headers</param>
        /// <returns>JSON response from server</returns>
        public Task<string> PostAsync(string command, string data = null, Dictionary<string, string> additionalHeaders = null)
            => ExecuteRequestAsync("POST", command, additionalHeaders, data, CancellationToken.None);

        /// <summary>
        /// Performs POST request to API; the request is aborted when <paramref name="cancellationToken"/> is cancelled
        /// </summary>
        public Task<string> PostAsync(string command, string data, Dictionary<string, string> additionalHeaders, CancellationToken cancellationToken)
            => ExecuteRequestAsync("POST", command, additionalHeaders, data, cancellationToken);

        /// <summary>
        /// Performs PUT request to API
        /// </summary>
        /// <param name="command">API command</param>
        /// <param name="data">Data to send</param>
        /// <param name="additionalHeaders">Additional headers</param>
        /// <returns>JSON response from server</returns>
        public Task<string> PutAsync(string command, string data = null, Dictionary<string, string> additionalHeaders = null)
            => ExecuteRequestAsync("PUT", command, additionalHeaders, data, CancellationToken.None);

        /// <summary>
        /// Performs PUT request to API; the request is aborted when <paramref name="cancellationToken"/> is cancelled
        /// </summary>
        public Task<string> PutAsync(string command, string data, Dictionary<string, string> additionalHeaders, CancellationToken cancellationToken)
            => ExecuteRequestAsync("PUT", command, additionalHeaders, data, cancellationToken);

        /// <summary>
        /// Performs DELETE request to API
        /// </summary>
        /// <param name="command">API command</param>
        /// <param name="additionalHeaders">Additional headers</param>
        /// <returns>JSON response from server</returns>
        public Task<string> DeleteAsync(string command, Dictionary<string, string> additionalHeaders = null)
            => ExecuteRequestAsync("DELETE", command, additionalHeaders, null, CancellationToken.None);

        /// <summary>
        /// Performs DELETE request to API; the request is aborted when <paramref name="cancellationToken"/> is cancelled
        /// </summary>
        public Task<string> DeleteAsync(string command, Dictionary<string, string> additionalHeaders, CancellationToken cancellationToken)
            => ExecuteRequestAsync("DELETE", command, additionalHeaders, null, cancellationToken);

        /// <summary>
        /// Encodes path in API format: replaces separators with |, then percent-encodes
        /// every folder/model name (segment between |) with <see cref="Uri.EscapeDataString(string)"/>.
        /// The | separator itself is kept as is.
        /// </summary>
        /// <param name="path">Path to file or folder with raw (unescaped) names, e.g. "|#Archive|Model.rvt"</param>
        /// <returns>URL path segment ready to put into a command, e.g. "|%23Archive|Model.rvt"</returns>
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

            // Names go into the URL as data: without escaping '#' cuts the request (fragment),
            // '?' starts a query string and '%XX' is decoded by the server into another character.
            var segments = path.Split('|');
            for (var i = 0; i < segments.Length; i++)
                segments[i] = Uri.EscapeDataString(segments[i]);

            return string.Join("|", segments);
        }

        /// <summary>
        /// Performs HTTP request to API
        /// </summary>
        /// <param name="method">HTTP method</param>
        /// <param name="command">API command</param>
        /// <param name="additionalHeaders">Additional headers</param>
        /// <param name="data">Data to send</param>
        /// <param name="cancellationToken">Aborts the request when cancelled</param>
        /// <returns>JSON response from server</returns>
        private async Task<string> ExecuteRequestAsync(string method, string command, Dictionary<string, string> additionalHeaders, string data, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var url = $"{_baseUrl}/{command}";
            var request = WebRequest.Create(url) as HttpWebRequest;
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
            request.Headers.Add("User-Name", EncodeHeaderValue(_userName));
            request.Headers.Add("User-Machine-Name", EncodeHeaderValue(machineName));
            request.Headers.Add("Operation-GUID", operationGuid);
            // Add additional headers if provided
            if (additionalHeaders != null)
            {
                foreach (var header in additionalHeaders)
                {
                    request.Headers.Add(header.Key, EncodeHeaderValue(header.Value));
                }
            }

            // HttpWebRequest.Timeout is ignored by the async methods on .NET Framework, so a set limit is enforced here for both;
            // on .NET 6/8 HttpWebRequest applies its own Timeout (default 100 s) too, so it gets the same value.
            var timeout = _timeout;
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                if (timeout.HasValue)
                {
                    request.Timeout = timeout.Value == System.Threading.Timeout.InfiniteTimeSpan
                        ? System.Threading.Timeout.Infinite
                        : (int)timeout.Value.TotalMilliseconds;
                    if (timeout.Value != System.Threading.Timeout.InfiniteTimeSpan)
                        cts.CancelAfter(timeout.Value);
                }
                using (cts.Token.Register(request.Abort))
                {
                    try
                    {
                        return await SendAsync(request, method, data).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (cts.IsCancellationRequested && !(ex is RevitServerApiException apiEx && apiEx.StatusCode.HasValue))
                    {
                        // The request was aborted by the caller's token or by the timeout
                        if (cancellationToken.IsCancellationRequested)
                            throw new OperationCanceledException("API request was cancelled", ex, cancellationToken);
                        throw new RevitServerApiException($"API request timed out after {timeout}", ex);
                    }
                }
            }
        }

        private static async Task<string> SendAsync(HttpWebRequest request, string method, string data)
        {
            if (!string.IsNullOrEmpty(data) && (method == "POST" || method == "PUT"))
            {
                var bytes = Encoding.UTF8.GetBytes(data);
                request.ContentLength = bytes.Length;
                using (var stream = await request.GetRequestStreamAsync().ConfigureAwait(false))
                {
                    await stream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
                }
            }
            try
            {
                using (var response = await request.GetResponseAsync().ConfigureAwait(false) as HttpWebResponse)
                using (var stream = response?.GetResponseStream())
                {
                    if (stream == null)
                        return null;
                    using (var reader = new StreamReader(stream))
                    {
                        return await reader.ReadToEndAsync().ConfigureAwait(false);
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
                        var errorContent = await reader.ReadToEndAsync().ConfigureAwait(false);
                        throw new RevitServerApiException($"API request failed with status {errorResponse.StatusCode}: {errorContent}", errorResponse.StatusCode, errorContent, ex);
                    }
                }
                throw new RevitServerApiException($"API request failed: {ex.Message}", ex);
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

        /// <summary>
        /// Creates an exception for an HTTP error response returned by the server
        /// </summary>
        public RevitServerApiException(string message, HttpStatusCode statusCode, string responseContent, Exception innerException)
            : base(message, innerException)
        {
            StatusCode = statusCode;
            ResponseContent = responseContent;
        }

        /// <summary>
        /// HTTP status code of the error response; null when the server returned no HTTP response
        /// </summary>
        public HttpStatusCode? StatusCode { get; }

        /// <summary>
        /// Body of the error response; null when the server returned no HTTP response
        /// </summary>
        public string ResponseContent { get; }
    }
}
