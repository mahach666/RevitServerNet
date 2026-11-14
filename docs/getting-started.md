---
title: Getting started
---

# Getting started

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

## Usage Examples

### Basic Setup

```csharp
using System;
using RevitServerNet;
using RevitServerNet.Extensions;   // all operations below are extension methods
using RevitServerNet.Models;       // ServerInfo, FolderInfo, OperationResult, ... (only needed when you name the types)

// Create API client
var api = new RevitServerApi(
    host: "your-revit-server.com",
    userName: "your-username",
    useHttps: true,        // Optional, default: false
    serverVersion: "2024"  // Optional, default: "2019"; must be a year >= 2012
);
```

### Server Operations

```csharp
// Get server information (GET serverproperties)
ServerInfo serverInfo = await api.GetServerInfoAsync();
// The server reports no name or version: Version is the year this client was created with, Name stays null
Console.WriteLine($"{serverInfo?.MachineName} {serverInfo?.Version}");

// Check that the server responds and returns non-empty properties.
// Never throws: any exception yields false.
bool isRunning = await api.IsServerRunningAsync();
```

Next: [REST API](rest-api.md), [Admin UI API](admin-ui-api.md), [Direct export](direct-export.md), [Versions and compatibility](versions-and-compatibility.md).
