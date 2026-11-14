<p align="center">
  <img src="https://raw.githubusercontent.com/mahach666/RevitServerNet/master/docs/images/revitservernet-banner.png" alt="RevitServerNet" width="640">
</p>

A .NET library for working with Revit Server REST API. Provides easy access to Revit Server operations including folder management, project operations, locking, and history tracking.

[![NuGet](https://img.shields.io/nuget/v/RevitServerNet?label=NuGet)](https://www.nuget.org/packages/RevitServerNet)
[![NuGet downloads](https://img.shields.io/nuget/dt/RevitServerNet)](https://www.nuget.org/packages/RevitServerNet)
[![CI](https://github.com/mahach666/RevitServerNet/actions/workflows/ci.yml/badge.svg)](https://github.com/mahach666/RevitServerNet/actions/workflows/ci.yml)
[![Docs](https://github.com/mahach666/RevitServerNet/actions/workflows/docs.yml/badge.svg)](https://mahach666.github.io/RevitServerNet/)
[![Ask DeepWiki](https://img.shields.io/badge/DeepWiki-Ask-blue)](https://deepwiki.com/mahach666/RevitServerNet)

Documentation: https://mahach666.github.io/RevitServerNet/

## Features

- **Server Management**: Get server information and status
- **Folder Operations**: Create, delete, and manage folders on Revit Server
- **Project Information**: Read a model's project information parameters
- **Locking**: Lock and unlock models and folders, cancel in-progress locks, list or delete descendent locks, and query server-wide and per-model locks
- **History Tracking**: Access project history and version information
- **Copy / Move**: Copy and move items on the server (`CopyItemAsync`, `MoveItemAsync`)
- **Recursive traversal**: `WalkAsync`, `GetAllModelsRecursiveAsync`, `GetAllFoldersRecursiveAsync`
- **Admin UI API**: `RevitServerUiApi` for `RevitServerAdmin{YEAR}/api/...` (model details, lock data, histories, tree, rename/delete, copy/move, lock/unlock, create folder)
- **Direct Export**: Export Revit Server models to RVT files over the Revit Server ModelService (net.tcp), without RevitServerTool.exe (net48/net6/net8)
- **RevitServerTool wrapper**: `RevitServerToolClient.CreateLocalModelAsync` for exporting through RevitServerTool.exe
- **Multi-Version Support**: REST and Admin UI clients for Revit Server 2012 and later; direct export for any server year that exposes `/ModelService{year}/`, with one bundled set of Autodesk client assemblies (verified live against Revit Server 2022-2025)
- **Async Operations**: All API calls are asynchronous for better performance

## Installation

Via Package Manager Console:
```powershell
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
using RevitServerNet.Extensions; // GetServerInfoAsync, ListFoldersAsync, GetFolderContentsAsync are extension methods

// Initialize API client
// serverVersion defaults to "2019": set it to your Revit Server year
var api = new RevitServerApi("your-server-host", "your-username", serverVersion: "2024");

// Get server information
var serverInfo = await api.GetServerInfoAsync();

// List subfolders of a folder (List<FolderInfo>, Path is filled in)
var folders = await api.ListFoldersAsync("|path|to|folder");

// Or everything in the folder (Folders, Models, Files)
var contents = await api.GetFolderContentsAsync("|path|to|folder");
```

## Supported versions

REST API and Admin UI API: Revit Server 2012 and later. Direct export: any server year that exposes `net.tcp://{host}/ModelService{year}/ModelService.svc/`, verified live on Revit Server 2022 to 2025. The package targets .NET Framework 4.8, .NET 6 and .NET 8; the direct export needs Windows. Details, requirements and the exact limits: [Versions and compatibility](https://mahach666.github.io/RevitServerNet/versions-and-compatibility.html).

## Documentation

- [Getting started](https://mahach666.github.io/RevitServerNet/getting-started.html)
- [REST API](https://mahach666.github.io/RevitServerNet/rest-api.html): `RevitServerApi` and the extension methods
- [Admin UI API](https://mahach666.github.io/RevitServerNet/admin-ui-api.html): `RevitServerUiApi`
- [Direct export](https://mahach666.github.io/RevitServerNet/direct-export.html): how it works, locks, `CreateLocal`, cancellation, bundled assemblies, timeouts
- [RevitServerTool wrapper](https://mahach666.github.io/RevitServerNet/revitservertool.html)
- [Error handling](https://mahach666.github.io/RevitServerNet/error-handling.html)
- [Versions and compatibility](https://mahach666.github.io/RevitServerNet/versions-and-compatibility.html)
- [Testing](https://mahach666.github.io/RevitServerNet/testing.html): unit, wire and live tests
- [API reference](https://mahach666.github.io/RevitServerNet/api/RevitServerNet.html)
- [DeepWiki](https://deepwiki.com/mahach666/RevitServerNet): an automatically generated overview of the repository
- [Changelog](https://github.com/mahach666/RevitServerNet/blob/master/CHANGES.md)

## Build and test

```powershell
dotnet build RevitServerNet.sln -c Release
dotnet test RevitServerNet.sln -c Release
```

The tests that need a live Revit Server are skipped unless the `RSN_TEST_RS_*` environment variables are set; see [Testing](https://mahach666.github.io/RevitServerNet/testing.html). Building needs Windows (net48 and the direct export).

## Contributing

Contributions are welcome. See [CONTRIBUTING.md](https://github.com/mahach666/RevitServerNet/blob/master/CONTRIBUTING.md).

## Security

Report vulnerabilities privately, see [SECURITY.md](https://github.com/mahach666/RevitServerNet/blob/master/SECURITY.md).

## Support

If you encounter any issues or have questions, please open an issue at https://github.com/mahach666/RevitServerNet/issues.

## License

This project is licensed under the MIT License - see the [LICENSE](https://github.com/mahach666/RevitServerNet/blob/master/LICENSE.txt) file for details.

RevitServerNet is not affiliated with, endorsed by or sponsored by Autodesk. Autodesk, Revit and Revit Server are trademarks of Autodesk, Inc. The package bundles unmodified Autodesk Revit Server client assemblies (the Revit 2024 set).
