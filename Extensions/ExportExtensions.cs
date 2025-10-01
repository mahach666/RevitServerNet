using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace RevitServerNet.Extensions
{
    /// <summary>
    /// Convenience extensions to export models using RS libraries directly.
    /// </summary>
    public static class ExportExtensions
    {
        /// <summary>
        /// Export a model using server host and version inferred from the API client.
        /// </summary>
        public static async Task<string> ExportModelAsync(this RevitServerApi api, string modelPath, string destinationFile, string assembliesPath = null, bool overwrite = false, IProgress<long> bytesProgress = null)
        {
            if (api == null) throw new ArgumentNullException(nameof(api));
            if (string.IsNullOrWhiteSpace(modelPath)) throw new ArgumentException("Model path is required", nameof(modelPath));
            if (string.IsNullOrWhiteSpace(destinationFile)) throw new ArgumentException("Destination file is required", nameof(destinationFile));

            // Infer version from BaseUrl
            var version = InferVersionFromBaseUrl(api.BaseUrl) ?? "2019";
            var host = InferHostFromBaseUrl(api.BaseUrl);
            if (string.IsNullOrWhiteSpace(host)) throw new InvalidOperationException("Cannot infer server host from RevitServerApi.BaseUrl");

            // Call public exporter
            var options = new ModelExporterOptions
            {
                ServerHost = host,
                ModelPipePath = modelPath,
                DestinationFile = destinationFile,
                RevitVersion = version,
                AssembliesPath = assembliesPath,
                Overwrite = overwrite,
            };
            return await ModelExporter.ExportAsync(options, bytesProgress);
        }

        private static string InferVersionFromBaseUrl(string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl)) return null;
            var m = Regex.Match(baseUrl, @"RevitServerAdminRESTService(\\d{4})", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value : null;
        }

        private static string InferHostFromBaseUrl(string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl)) return null;
            var m = Regex.Match(baseUrl, @"^(https?://)([^/]+)", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[2].Value : null;
        }
    }
}


