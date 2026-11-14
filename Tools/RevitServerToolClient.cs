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
        // RevitServerTool exits with 0 also when it fails (for example for an unknown model) and then prints a line "ERROR: ..."
        public bool Success => ExitCode == 0 && ErrorMessage == null;

        // The first output line of the tool that starts with "ERROR:", or null
        public string ErrorMessage =>
            new[] { StandardOutput, StandardError }
                .Where(text => !string.IsNullOrEmpty(text))
                .SelectMany(text => text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
                .Select(line => line.Trim())
                .FirstOrDefault(line => line.StartsWith("ERROR:", StringComparison.Ordinal));
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
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                // An unreadable Program Files folder or a versionHint that is not a valid path segment: no tool found.
            }
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

            if (timeoutMs < -1)
                throw new ArgumentOutOfRangeException(nameof(timeoutMs), "Timeout must be non-negative, or -1 for no timeout.");

            // A file name without a directory part is relative to the current directory.
            var destinationPath = Path.GetFullPath(destinationFile);
            if (string.IsNullOrEmpty(Path.GetFileName(destinationPath)))
                throw new ArgumentException("Destination file path must end with a file name", nameof(destinationFile));
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));

            return await ExecuteProcessAsync(toolPath, BuildCreateLocalArguments(serverHost, modelRelativePath, destinationPath, overwrite), timeoutMs).ConfigureAwait(false);
        }

        /// <summary>
        /// Command line of "createLocalRvt": every value quoted and escaped, so that it reaches the tool as one argument
        /// (a host or path with spaces or quotes cannot add or change options).
        /// </summary>
        internal static string BuildCreateLocalArguments(string serverHost, string modelRelativePath, string destinationPath, bool overwrite)
        {
            var args = "createLocalRvt " + QuoteArgument(modelRelativePath) + " -s " + QuoteArgument(serverHost) + " -d " + QuoteArgument(destinationPath);
            return overwrite ? args + " -o" : args;
        }

        /// <summary>
        /// Quotes one argument for a Windows command line (the rules of CommandLineToArgvW and the C runtime): embedded quotes are escaped,
        /// and the backslashes before a quote, including the closing one, are doubled.
        /// </summary>
        internal static string QuoteArgument(string argument)
        {
            var quoted = new StringBuilder("\"");
            var backslashes = 0;
            foreach (var c in argument)
            {
                if (c == '\\')
                {
                    backslashes++;
                    continue;
                }
                if (c == '"')
                    quoted.Append('\\', backslashes * 2 + 1);
                else
                    quoted.Append('\\', backslashes);
                quoted.Append(c);
                backslashes = 0;
            }
            return quoted.Append('\\', backslashes * 2).Append('"').ToString();
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

        private const int KillWaitMs = 5000;

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

            using (var process = new Process { StartInfo = psi })
            {
                var stdout = new StringBuilder();
                var stderr = new StringBuilder();
                process.OutputDataReceived += (s, e) => { if (e.Data != null) lock (stdout) stdout.AppendLine(e.Data); };
                process.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (stderr) stderr.AppendLine(e.Data); };

                if (!process.Start())
                    throw new RevitServerToolException("Failed to start RevitServerTool process.");

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                var exited = await Task.Run(() => process.WaitForExit(timeoutMs)).ConfigureAwait(false);
                if (!exited)
                {
                    try
                    {
                        process.Kill();
                    }
                    catch (Exception ex) when (ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception)
                    {
                        // It exited between the timeout and the kill, or is already terminating.
                    }
                    // Wait (bounded) for the process to be gone before it is disposed. The overload with a timeout does not wait for the
                    // end of the redirected output, which a child process of the tool may keep open.
                    await Task.Run(() => process.WaitForExit(KillWaitMs)).ConfigureAwait(false);
                    throw new TimeoutException($"RevitServerTool timed out after {timeoutMs} ms and was killed.");
                }

                // WaitForExit(int) can return before the redirected output has been read to its end; this overload waits for it.
                await Task.Run(() => process.WaitForExit()).ConfigureAwait(false);
                return new RevitServerToolResult
                {
                    ExitCode = process.ExitCode,
                    StandardOutput = ReadAll(stdout),
                    StandardError = ReadAll(stderr)
                };
            }
        }

        private static string ReadAll(StringBuilder builder)
        {
            lock (builder) return builder.ToString();
        }
    }
}
