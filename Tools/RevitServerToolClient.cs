using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace RevitServerNet.Tools
{
    public class RevitServerToolException : Exception
    {
        public RevitServerToolException(string message) : base(message) { }
        public RevitServerToolException(string message, Exception inner) : base(message, inner) { }
    }

    public class RevitServerToolResult
    {
        public int ExitCode { get; set; }
        public string StandardOutput { get; set; }
        public string StandardError { get; set; }
        public bool Success => ExitCode == 0;
    }

    public static class RevitServerToolClient
    {
        public static string TryLocateToolPath(string versionHint = null)
        {
            try
            {
                var envPath = Environment.GetEnvironmentVariable("REVITSERVER_TOOL_PATH");
                if (!string.IsNullOrWhiteSpace(envPath) && File.Exists(envPath))
                    return envPath;

                string[] roots = new[]
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    Environment.GetEnvironmentVariable("ProgramW6432"),
                    Environment.GetEnvironmentVariable("ProgramFiles(x86)")
                }.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct().ToArray();

                if (!string.IsNullOrWhiteSpace(versionHint))
                {
                    foreach (var root in roots)
                    {
                        var p = Path.Combine(root, $"Autodesk", $"Revit {versionHint}", "RevitServerToolCommand", "RevitServerTool.exe");
                        if (File.Exists(p)) return p;
                    }
                }

                foreach (var root in roots)
                {
                    var autodeskDir = Path.Combine(root, "Autodesk");
                    if (!Directory.Exists(autodeskDir)) continue;
                    var candidates = Directory.GetDirectories(autodeskDir, "Revit *", SearchOption.TopDirectoryOnly);
                    foreach (var dir in candidates.OrderByDescending(d => d))
                    {
                        var p = Path.Combine(dir, "RevitServerToolCommand", "RevitServerTool.exe");
                        if (File.Exists(p)) return p;
                    }
                }
            }
            catch { }
            return null;
        }

        public static async Task<RevitServerToolResult> CreateLocalModelAsync(string toolPath
            , string serverHost
            , string modelRelativePath
            , string destinationFile
            , bool overwrite = false
            , int timeoutMs = 600000)
        {
            if (string.IsNullOrWhiteSpace(toolPath) || !File.Exists(toolPath))
                throw new RevitServerToolException("RevitServerTool.exe not found. Provide valid toolPath or set REVITSERVER_TOOL_PATH.");
            if (string.IsNullOrWhiteSpace(serverHost))
                throw new ArgumentException("Server host is required", nameof(serverHost));
            if (string.IsNullOrWhiteSpace(modelRelativePath))
                throw new ArgumentException("Model relative path is required", nameof(modelRelativePath));
            if (string.IsNullOrWhiteSpace(destinationFile))
                throw new ArgumentException("Destination file path is required", nameof(destinationFile));

            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile));

            var argsBuilder = new StringBuilder();
            argsBuilder.Append("createLocalRvt ");
            argsBuilder.Append('"').Append(modelRelativePath).Append('"');
            argsBuilder.Append(" -s ").Append(serverHost);
            argsBuilder.Append(" -d ").Append('"').Append(destinationFile).Append('"');
            if (overwrite)
                argsBuilder.Append(" -o");

            return await ExecuteProcessAsync(toolPath, argsBuilder.ToString(), timeoutMs);
        }

        public static string ConvertPipePathToRelativeWindowsPath(string pipePath)
        {
            if (string.IsNullOrWhiteSpace(pipePath)) return pipePath;
            var p = pipePath.Trim();
            if (p.StartsWith("|")) p = p.Substring(1);
            return p.Replace('|', Path.DirectorySeparatorChar);
        }

        public static string ParseVersionFromBaseUrl(string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl)) return null;
            var m = Regex.Match(baseUrl, @"RevitServerAdminRESTService(\d{4})", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value : null;
        }

        private static async Task<RevitServerToolResult> ExecuteProcessAsync(string fileName, string arguments, int timeoutMs)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using (var process = new Process { StartInfo = psi, EnableRaisingEvents = true })
            {
                var tcs = new TaskCompletionSource<int>();
                var stdout = new StringBuilder();
                var stderr = new StringBuilder();
                process.OutputDataReceived += (s, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
                process.ErrorDataReceived += (s, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

                if (!process.Start())
                    throw new RevitServerToolException("Failed to start RevitServerTool process.");

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                using (var cts = new System.Threading.CancellationTokenSource())
                {
                    var registration = cts.Token.Register(() =>
                    {
                        try { if (!process.HasExited) process.Kill(); } catch { }
                        tcs.TrySetException(new TimeoutException("RevitServerTool timed out."));
                    });
                    cts.CancelAfter(timeoutMs);

                    process.Exited += (s, e) =>
                    {
                        try { registration.Dispose(); } catch { }
                        tcs.TrySetResult(process.ExitCode);
                    };

                    var exitCode = await tcs.Task.ConfigureAwait(false);
                    return new RevitServerToolResult
                    {
                        ExitCode = exitCode,
                        StandardOutput = stdout.ToString(),
                        StandardError = stderr.ToString()
                    };
                }
            }
        }
    }
}


