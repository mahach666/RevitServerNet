using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace RevitServerNet.Enterprise
{
	internal sealed class RsAssemblies
	{
		private readonly Dictionary<string, Assembly> _nameToAssembly = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, Type> _typeCache = new Dictionary<string, Type>(StringComparer.Ordinal);

		public string BaseDirectory { get; }

		public RsAssemblies(string baseDirectory)
		{
			BaseDirectory = baseDirectory;
		}

		public void Add(Assembly asm)
		{
			if (asm == null) return;
			var name = Path.GetFileName(asm.Location);
			if (!string.IsNullOrEmpty(name))
			{
				_nameToAssembly[name] = asm;
			}
		}

		public Type GetType(string fullTypeName)
		{
			if (string.IsNullOrWhiteSpace(fullTypeName)) return null;
			if (_typeCache.TryGetValue(fullTypeName, out var cached)) return cached;
			foreach (var asm in _nameToAssembly.Values)
			{
				var t = asm.GetType(fullTypeName, throwOnError: false, ignoreCase: false);
				if (t != null)
				{
					_typeCache[fullTypeName] = t;
					return t;
				}
			}
			return null;
		}

		public Assembly FindAssembly(string partialFileName)
		{
			if (string.IsNullOrWhiteSpace(partialFileName)) return null;
			if (_nameToAssembly.TryGetValue(partialFileName, out var asm)) return asm;
			return null;
		}
	}

	internal static class RsAssemblyLoader
	{
		private static readonly string[] RequiredAssemblies = new[]
		{
			"Autodesk.RevitServer.Social.dll",
			"RS.Enterprise.Common.ClientServer.DataContract.dll",
			"RS.Enterprise.Common.ClientServer.Helper.dll",
			"RS.Enterprise.Common.ClientServer.Proxy.dll",
			"RS.Enterprise.Common.ClientServer.ServiceContract.Local.dll",
			"RS.Enterprise.Common.ClientServer.ServiceContract.Model.dll",
			"Castle.Core.dll",
			"Castle.Windsor.dll"
		};

		public static RsAssemblies Load(string revitVersion, string assembliesPath = null)
		{
			var baseDir = !string.IsNullOrWhiteSpace(assembliesPath) && Directory.Exists(assembliesPath)
				? assembliesPath
				: TryLocateAssembliesDirectory(revitVersion);
			if (string.IsNullOrWhiteSpace(baseDir) || !Directory.Exists(baseDir))
				throw new InvalidOperationException($"RS assemblies directory not found. Provide assembliesPath explicitly. Version hint='{revitVersion}'.");

			// Ensure assembly resolve for dependencies within the same folder
			AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
			{
				try
				{
					var name = new AssemblyName(e.Name).Name + ".dll";
					var candidate = Directory.GetFiles(baseDir, name, SearchOption.AllDirectories).FirstOrDefault();
					if (candidate != null && File.Exists(candidate))
						return Assembly.LoadFrom(candidate);
				}
				catch { }
				return null;
			};

			var loaded = new RsAssemblies(baseDir);
			foreach (var fileName in RequiredAssemblies)
			{
				var candidates = Directory.GetFiles(baseDir, fileName, SearchOption.AllDirectories);
				string path = null;
				if (candidates != null && candidates.Length > 0)
				{
					// Prefer a build from a folder named 'ChangedLibraries' if present (used by some distributions)
					path = candidates
						.OrderByDescending(p => p.IndexOf("ChangedLibraries", StringComparison.OrdinalIgnoreCase) >= 0)
						.ThenByDescending(p => new FileInfo(p).LastWriteTimeUtc)
						.FirstOrDefault();
				}
				if (!string.IsNullOrEmpty(path) && File.Exists(path))
				{
					var asm = Assembly.LoadFrom(path);
					loaded.Add(asm);
				}
			}
			// Sanity check: must have proxy and service contract
			var missing = new List<string>();
			if (loaded.FindAssembly("RS.Enterprise.Common.ClientServer.Proxy.dll") == null) missing.Add("RS.Enterprise.Common.ClientServer.Proxy.dll");
			if (loaded.FindAssembly("RS.Enterprise.Common.ClientServer.ServiceContract.Model.dll") == null) missing.Add("RS.Enterprise.Common.ClientServer.ServiceContract.Model.dll");
			if (missing.Count > 0)
				throw new InvalidOperationException($"Failed to load required RS assemblies: {string.Join(", ", missing)}. Base='{baseDir}'.");

			return loaded;
		}

		public static string TryLocateAssembliesDirectory(string versionHint)
		{
			// 1) Prefer assemblies packaged alongside the app under RSAssemblies/<version>
			var baseDir = AppContext.BaseDirectory;
			try
			{
				if (!string.IsNullOrWhiteSpace(versionHint))
				{
					var packaged = Path.Combine(baseDir, "RSAssemblies", versionHint);
					var packagedFound = FindDirWithProxyDll(packaged);
					if (!string.IsNullOrWhiteSpace(packagedFound)) return packagedFound;
				}
				// Also allow flat RSAssemblies directory without explicit version folder
				var packagedFlat = Path.Combine(baseDir, "RSAssemblies");
				var packagedFlatFound = FindDirWithProxyDll(packagedFlat);
				if (!string.IsNullOrWhiteSpace(packagedFlatFound)) return packagedFlatFound;
			}
			catch { }

			// 1.1) Assemblies packaged next to this library's DLL (when referenced as project or via NuGet)
			try
			{
				var libDir = Path.GetDirectoryName(typeof(RsAssemblyLoader).Assembly.Location);
				if (!string.IsNullOrWhiteSpace(libDir))
				{
					if (!string.IsNullOrWhiteSpace(versionHint))
					{
						var packaged = Path.Combine(libDir, "RSAssemblies", versionHint);
						var found = FindDirWithProxyDll(packaged);
						if (!string.IsNullOrWhiteSpace(found)) return found;
					}
					var packagedFlat = Path.Combine(libDir, "RSAssemblies");
					var foundFlat = FindDirWithProxyDll(packagedFlat);
					if (!string.IsNullOrWhiteSpace(foundFlat)) return foundFlat;
				}
			}
			catch { }

			// 2) Fallback to scanning Autodesk install locations
			var roots = new List<string>();
			void Add(string p) { if (!string.IsNullOrWhiteSpace(p)) roots.Add(p); }
			Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
			Add(Environment.GetEnvironmentVariable("ProgramW6432"));
			Add(Environment.GetEnvironmentVariable("ProgramFiles(x86)"));

			foreach (var root in roots.Distinct())
			{
				try
				{
					// Common Revit Server install path
					if (!string.IsNullOrWhiteSpace(versionHint))
					{
						var rsDir = Path.Combine(root, "Autodesk", $"Revit Server {versionHint}");
						if (Directory.Exists(rsDir))
						{
							var found = FindDirWithProxyDll(rsDir);
							if (found != null) return found;
						}
						var revitDir = Path.Combine(root, "Autodesk", $"Revit {versionHint}");
						if (Directory.Exists(revitDir))
						{
							var found = FindDirWithProxyDll(revitDir);
							if (found != null) return found;
						}
					}
					// Broad search
					var autodesk = Path.Combine(root, "Autodesk");
					if (!Directory.Exists(autodesk)) continue;
					foreach (var dir in Directory.EnumerateDirectories(autodesk, "Revit*", SearchOption.TopDirectoryOnly))
					{
						var found = FindDirWithProxyDll(dir);
						if (found != null) return found;
					}
					foreach (var dir in Directory.EnumerateDirectories(autodesk, "Revit Server*", SearchOption.TopDirectoryOnly))
					{
						var found = FindDirWithProxyDll(dir);
						if (found != null) return found;
					}
				}
				catch { }
			}
			return null;
		}

		private static string FindDirWithProxyDll(string root)
		{
			try
			{
				var proxy = Directory.GetFiles(root, "RS.Enterprise.Common.ClientServer.Proxy.dll", SearchOption.AllDirectories).FirstOrDefault();
				if (proxy != null)
					return Path.GetDirectoryName(proxy);
			}
			catch { }
			return null;
		}
	}
}


