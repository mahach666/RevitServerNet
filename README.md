# RevitServerNet

A .NET library for working with Revit Server REST API. Provides easy access to Revit Server operations including folder management, project operations, locking, and history tracking.

## Features

- **Server Management**: Get server information and status
- **Folder Operations**: Create, delete, and manage folders on Revit Server
- **Project Management**: Handle project operations and metadata
- **Locking System**: Manage file locks and permissions
- **History Tracking**: Access project history and version information
- **Direct Export**: Convert Revit Server models to RVT using RS assemblies (net48/net6/net8)
- **Multi-Version Support**: Supports Revit Server versions from 2012 to 2026
- **Async Operations**: All API calls are asynchronous for better performance

## Installation

```bash
Install-Package RevitServerNet
```

Or via NuGet Package Manager:
```
RevitServerNet
```

Or via .NET CLI:
```bash
dotnet add package RevitServerNet
```

## Quick Start

```csharp
using RevitServerNet;

// Initialize API client
var api = new RevitServerApi("your-server-host", "your-username");

// Get server information
var serverInfo = await api.GetServerInfoAsync();

// List folders
var folders = await api.GetFoldersAsync("/path/to/folder");
```

## Supported Revit Server Versions

- Revit Server 2012-2026

## Requirements

- .NET Framework 4.8 or .NET 6/8
- Newtonsoft.Json 13.0.3

## Usage Examples

### Basic Setup

```csharp
using RevitServerNet;

// Create API client
var api = new RevitServerApi(
    host: "your-revit-server.com", 
    userName: "your-username",
    useHttps: true,  // Optional, default: false
    serverVersion: "2024"  // Optional, default: "2019"
);
```

### Server Operations

```csharp
// Get server information
var serverInfo = await api.GetServerInfoAsync();

// Get server status
var status = await api.GetServerStatusAsync();
```

### Folder Operations

```csharp
// List folders in a directory
var folders = await api.GetFoldersAsync("/Projects");

// Create a new folder
await api.CreateFolderAsync("/Projects/NewProject");

// Delete a folder
await api.DeleteFolderAsync("/Projects/OldProject");
```

### Project Operations

```csharp
// Get project information
var projectInfo = await api.GetProjectInfoAsync("/Projects/MyProject");

// Get project history
var history = await api.GetProjectHistoryAsync("/Projects/MyProject");
```

### Locking Operations

```csharp
// Lock a file
await api.LockFileAsync("/Projects/MyProject/file.rvt");

// Unlock a file
await api.UnlockFileAsync("/Projects/MyProject/file.rvt");

// Get lock information
var lockInfo = await api.GetLockInfoAsync("/Projects/MyProject/file.rvt");
```

## Direct export from Revit Server (RS Enterprise API)

RevitServerNet can export a model directly to a local RVT file using the RS Enterprise assemblies — without calling `RevitServerTool.exe`. This works on **.NET Framework 4.8, .NET 6, and .NET 8** on Windows.

Prerequisites:
- Windows with .NET Framework 4.8 or .NET 6/8
- Access to the Revit Server host
- RS assemblies for the target version available in `RSAssemblies/<year>` (shipped in the repo) or in a custom folder you pass via `AssembliesPath`

### Minimal example (direct call)
```csharp
using RevitServerNet;

var options = new ModelExporterOptions
{
    ServerHost = "revit-server.local",
    ModelPipePath = "|Projects|Demo|Model.rvt",          // pipe or Windows-style path
    DestinationFile = @"C:\Exports\Demo.rvt",
    RevitVersion = "2024",
    AssembliesPath = @"C:\path\to\RSAssemblies\2024",    // optional: autodetected if bundled folder is present
    Overwrite = true
};

var resultPath = await ModelExporter.ExportAsync(
    options,
    new Progress<long>(bytes => Console.WriteLine($"Downloaded {bytes} bytes"))
);
Console.WriteLine($"Exported to: {resultPath}");
```

### Using `RevitServerApi` extension (infers host/version)
```csharp
using RevitServerNet;
using RevitServerNet.Extensions;

var api = new RevitServerApi("revit-server.local", "user", serverVersion: "2024");
await api.ExportModelAsync(
    modelPath: "|Projects|Demo|Model.rvt",
    destinationFile: @"C:\Exports\Demo.rvt",
    overwrite: true
);
```
Notes:
- The extension infers host/version from `api.BaseUrl`. It throws `PlatformNotSupportedException` on targets other than net48/net6/net8.
- Use pipe-format paths (`|Projects|...|file.rvt`) or Windows-style relative paths (`Projects\...\file.rvt`).

### Reference implementation
See `AnlaxRSExportService` (net48 WebAPI + CLI) for a working sample that wraps `ModelExporter`:
- CLI: `AnlaxRSExportService.exe --export <server> <pipePath> <localPath> <revitVersion> [rsAssembliesPath]`
- HTTP: `POST http://localhost:3074/api/export/export` with JSON matching `ModelExporterOptions` fields.

## API Reference

### RevitServerApi Class

Main class for interacting with Revit Server REST API.

#### Constructor

```csharp
public RevitServerApi(string host, string userName, bool useHttps = false, string serverVersion = "2019")
```

#### Properties

- `BaseUrl`: Gets the base URL for API requests
- `UserName`: Gets the user name used for API requests

#### Methods

- `GetAsync(string command, Dictionary<string, string> additionalHeaders = null)`: Performs GET request
- `PostAsync(string command, string data = null, Dictionary<string, string> additionalHeaders = null)`: Performs POST request
- `PutAsync(string command, string data = null, Dictionary<string, string> additionalHeaders = null)`: Performs PUT request
- `DeleteAsync(string command, Dictionary<string, string> additionalHeaders = null)`: Performs DELETE request

### Extension Methods

The library provides extension methods for common operations:

#### ServerExtensions
- `GetServerInfoAsync(this RevitServerApi api)`
- `GetServerStatusAsync(this RevitServerApi api)`

#### FolderExtensions
- `GetFoldersAsync(this RevitServerApi api, string path)`
- `CreateFolderAsync(this RevitServerApi api, string path)`
- `DeleteFolderAsync(this RevitServerApi api, string path)`

#### ProjectExtensions
- `GetProjectInfoAsync(this RevitServerApi api, string path)`

#### HistoryExtensions
- `GetProjectHistoryAsync(this RevitServerApi api, string path)`

#### LockingExtensions
- `LockFileAsync(this RevitServerApi api, string path)`
- `UnlockFileAsync(this RevitServerApi api, string path)`
- `GetLockInfoAsync(this RevitServerApi api, string path)`

## Error Handling

The library throws `RevitServerApiException` for API-related errors:

```csharp
try
{
    var folders = await api.GetFoldersAsync("/Projects");
}
catch (RevitServerApiException ex)
{
    Console.WriteLine($"API Error: {ex.Message}");
}
```

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE.txt) file for details.

## Support

If you encounter any issues or have questions, please open an issue on the GitHub repository.
