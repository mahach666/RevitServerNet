using System;
using System.Threading;
using System.Threading.Tasks;
//#if NETFRAMEWORK || NET6_0 || NET8_0
using RevitServerNet.Enterprise;
//#endif

namespace RevitServerNet
{
    /// <summary>
    /// Public options for direct library-based model export from Revit Server.
    /// </summary>
    public sealed class ModelExporterOptions
    {
        /// <summary>
        /// Revit Server host name or IP (e.g. "revit-server.company.local").
        /// </summary>
        public string ServerHost { get; set; }

        /// <summary>
        /// Model path in Revit Server pipe format (e.g. "|Projects|MyProj|model.rvt") or Windows-style relative path (e.g. "Projects\\MyProj\\model.rvt").
        /// </summary>
        public string ModelPipePath { get; set; }

        /// <summary>
        /// Destination RVT file path to create.
        /// </summary>
        public string DestinationFile { get; set; }

        /// <summary>
        /// Revit/Revit Server version (e.g. "2022", "2024").
        /// </summary>
        public string RevitVersion { get; set; }

        /// <summary>
        /// Optional absolute path to a directory containing required RS assemblies; if omitted, loader will attempt to auto-discover.
        /// </summary>
        public string AssembliesPath { get; set; }

        /// <summary>
        /// Overwrite destination file if it already exists (default: false).
        /// </summary>
        public bool Overwrite { get; set; }
    }

    /// <summary>
    /// Public entry point for exporting a Revit Server model to a local RVT file using RS libraries directly (no RevitServerTool.exe).
    /// </summary>
    public static class ModelExporter
    {
        /// <summary>
        /// Export a Revit Server model to a local RVT file using RS libraries directly.
        /// </summary>
        /// <param name="options">Export options (required)</param>
        /// <param name="bytesProgress">Optional progress reporter with downloaded bytes</param>
        /// <param name="cancellationToken">Cancellation token (not currently used internally)</param>
        /// <returns>Path to the created RVT file</returns>
        public static async Task<string> ExportAsync(ModelExporterOptions options, IProgress<long> bytesProgress = null, CancellationToken cancellationToken = default)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrWhiteSpace(options.ServerHost)) throw new ArgumentException("ServerHost is required", nameof(options.ServerHost));
            if (string.IsNullOrWhiteSpace(options.ModelPipePath)) throw new ArgumentException("ModelPipePath is required", nameof(options.ModelPipePath));
            if (string.IsNullOrWhiteSpace(options.DestinationFile)) throw new ArgumentException("DestinationFile is required", nameof(options.DestinationFile));
            if (string.IsNullOrWhiteSpace(options.RevitVersion)) throw new ArgumentException("RevitVersion is required", nameof(options.RevitVersion));
            
//#if NETFRAMEWORK || NET6_0 || NET8_0
            // Map public options to internal exporter options and execute
            var internalOptions = new RsModelExporterOptions
            {
                ServerHost = options.ServerHost,
                ModelPipePath = options.ModelPipePath,
                DestinationFile = options.DestinationFile,
                RevitVersion = options.RevitVersion,
                AssembliesPath = options.AssembliesPath,
                Overwrite = options.Overwrite
            };

            var exporter = new RsModelExporter();
            return await exporter.ExportAsync(internalOptions, bytesProgress);
//#else
//            throw new PlatformNotSupportedException("Direct RS library export is only available on .NET Framework, .NET 6 or .NET 8. Use REST APIs on other targets.");
//#endif
        }
    }
}



