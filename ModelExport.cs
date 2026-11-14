using System;
using System.Threading;
using System.Threading.Tasks;
using RevitServerNet.Enterprise;

namespace RevitServerNet
{
    /// <summary>
    /// Public options for direct library-based model export from Revit Server.
    /// </summary>
    public sealed class ModelExporterOptions
    {
        /// <summary>
        /// Revit Server host name or IP (e.g. "revit-server.company.local"), optionally with the net.tcp port ("host:808").
        /// </summary>
        public string ServerHost { get; set; }

        /// <summary>
        /// Model path in Revit Server pipe format (e.g. "|Projects|MyProj|model.rvt") or Windows-style relative path (e.g. "Projects\\MyProj\\model.rvt").
        /// Leading, trailing and doubled separators are ignored, and '/' is a separator too (as in the REST client).
        /// </summary>
        public string ModelPipePath { get; set; }

        /// <summary>
        /// Destination RVT file path to create. It is checked before anything is downloaded: it must end with a valid file name, must not be an
        /// existing folder, and with <see cref="Overwrite"/> an existing file must be writable and not open in another process.
        /// </summary>
        public string DestinationFile { get; set; }

        /// <summary>
        /// Revit Server version as a four-digit year (e.g. "2022", "2024"); selects the endpoint /ModelService{year}/.
        /// </summary>
        public string RevitVersion { get; set; }

        /// <summary>
        /// Optional directory, searched first, that directly contains the Revit Server client assemblies (the six files listed in the README).
        /// It is used only if it contains all of them (on .NET Framework, also only if every file path is at most 259 characters).
        /// Otherwise it is skipped, like a path that does not exist, and the search continues with &lt;app&gt;\RSAssemblies,
        /// &lt;folder of RevitServerNet.dll&gt;\RSAssemblies, &lt;folder of the executable&gt;\RSAssemblies (.NET 6 and later),
        /// then &lt;Program Files&gt;\Autodesk\Revit {year}\RevitServerToolCommand.
        /// </summary>
        public string AssembliesPath { get; set; }

        /// <summary>
        /// Overwrite destination file if it already exists (default: false).
        /// </summary>
        public bool Overwrite { get; set; }

        /// <summary>
        /// Write the BasicFileInfo of a local copy, as RevitServerTool createLocalRVT does: worksharing state "Created Local",
        /// central path RSN://{ServerHost}/{model path} (ServerHost as given, including a port: RevitServerTool writes the name it was given to -s),
        /// the server model as central, a new model identity.
        /// Default false: the file keeps the server's BasicFileInfo (central model), as in earlier versions.
        /// </summary>
        public bool CreateLocal { get; set; }
    }

    /// <summary>
    /// Public entry point for exporting a Revit Server model to a local RVT file using RS libraries directly (no RevitServerTool.exe).
    /// </summary>
    public static class ModelExporter
    {
        /// <summary>
        /// Export a Revit Server model to a local RVT file using RS libraries directly.
        /// Overlapping exports of the same model are serialized only within one Windows session and only when ServerHost is spelled
        /// the same and ModelPipePath has the same case (path separators are normalized); otherwise one export can release the other's
        /// server read lock.
        /// </summary>
        /// <param name="options">Export options (required)</param>
        /// <param name="bytesProgress">Optional progress reporter: bytes downloaded so far of the current model data file.
        /// Called on a thread-pool thread: use System.Progress&lt;T&gt; or marshal to the UI thread yourself.</param>
        /// <param name="cancellationToken">Cancels the export, also while it is still connecting to an unreachable server; the server read lock
        /// is still released and the temp folder deleted</param>
        /// <returns>Path to the created RVT file</returns>
        public static async Task<string> ExportAsync(ModelExporterOptions options, IProgress<long> bytesProgress = null, CancellationToken cancellationToken = default)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrWhiteSpace(options.ServerHost)) throw new ArgumentException("ServerHost is required", nameof(options.ServerHost));
            if (string.IsNullOrWhiteSpace(options.ModelPipePath)) throw new ArgumentException("ModelPipePath is required", nameof(options.ModelPipePath));
            if (string.IsNullOrWhiteSpace(options.DestinationFile)) throw new ArgumentException("DestinationFile is required", nameof(options.DestinationFile));
            if (string.IsNullOrWhiteSpace(options.RevitVersion)) throw new ArgumentException("RevitVersion is required", nameof(options.RevitVersion));

            var internalOptions = new RsModelExporterOptions
            {
                ServerHost = options.ServerHost,
                ModelPipePath = options.ModelPipePath,
                DestinationFile = options.DestinationFile,
                RevitVersion = options.RevitVersion,
                AssembliesPath = options.AssembliesPath,
                Overwrite = options.Overwrite,
                CreateLocal = options.CreateLocal
            };

            var exporter = new RsModelExporter();
            return await exporter.ExportAsync(internalOptions, bytesProgress, cancellationToken).ConfigureAwait(false);
        }
    }
}
