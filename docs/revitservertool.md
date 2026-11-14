---
title: RevitServerTool wrapper
---

# RevitServerTool wrapper

## RevitServerToolClient (namespace `RevitServerNet.Tools`)

Static wrapper around `RevitServerTool.exe createLocalRvt`:

- `static string TryLocateToolPath(string versionHint = null)`: checks the `REVITSERVER_TOOL_PATH` environment variable first, then `Autodesk\Revit <versionHint>\RevitServerToolCommand\RevitServerTool.exe`, then any `Autodesk\Revit *` folder under the Program Files folders. Returns `null` if the tool is not found.
- `static Task<RevitServerToolResult> CreateLocalModelAsync(string toolPath, string serverHost, string modelRelativePath, string destinationFile, bool overwrite = false, int timeoutMs = 600000)`: runs the tool and returns its result. It does not throw on a non-zero exit code, so check `Success`. Throws `ArgumentException` if `serverHost`, `modelRelativePath` or `destinationFile` is empty, or if the full destination path has no file name (for example it ends with a separator); `ArgumentOutOfRangeException` if `timeoutMs` is less than -1; `RevitServerToolException` if `toolPath` does not exist; and `System.ComponentModel.Win32Exception` if the process cannot be started (for example `toolPath` is not an executable). Creating the destination directory can throw `IOException`, `UnauthorizedAccessException`, `PathTooLongException` or `NotSupportedException`. The task faults with `TimeoutException` after `timeoutMs`: the tool is killed and the wait for it to exit is limited to 5 seconds.
- `static string ConvertPipePathToRelativeWindowsPath(string pipePath)`: converts `|Projects|Demo|Model.rvt` to `Projects\Demo\Model.rvt`.
- `static string ParseVersionFromBaseUrl(string baseUrl)`: extracts the year from `RevitServerAdminRESTService<year>`, or returns `null`.
- `RevitServerToolResult`: `ExitCode`, `StandardOutput`, `StandardError`, `ErrorMessage` (the first output line that starts with `ERROR:`, or null), `Success` (`ExitCode == 0` and no `ERROR:` line: RevitServerTool exits with 0 also when it fails, for example for a model that does not exist).
- The server host, model path and destination are passed as separately quoted and escaped arguments; a destination without a directory is relative to the current directory.
- `RevitServerToolException`: thrown for tool-level failures.
