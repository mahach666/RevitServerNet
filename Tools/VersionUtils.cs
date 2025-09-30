using System.Text.RegularExpressions;

namespace RevitServerNet.Tools
{
	internal static class VersionUtils
	{
		public static string ParseVersionFromBaseUrl(string baseUrl)
		{
			if (string.IsNullOrWhiteSpace(baseUrl)) return null;
			var m = Regex.Match(baseUrl, @"RevitServerAdminRESTService(\d{4})", RegexOptions.IgnoreCase);
			return m.Success ? m.Groups[1].Value : null;
		}
	}
}


