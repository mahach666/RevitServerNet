using System.IO;

namespace RevitServerNet.Tools
{
	internal static class PathUtils
	{
		public static string ConvertPipePathToRelativeWindowsPath(string pipePath)
		{
			if (string.IsNullOrWhiteSpace(pipePath)) return pipePath;
			var p = pipePath.Trim();
			if (p.StartsWith("|")) p = p.Substring(1);
			return p.Replace('|', Path.DirectorySeparatorChar);
		}
	}
}


