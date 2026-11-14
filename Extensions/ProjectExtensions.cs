using Newtonsoft.Json.Linq;
using RevitServerNet.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RevitServerNet.Extensions
{
    // Extensions for working with project information
    public static class ProjectExtensions
    {
        // '@typeOfParameter' is localized by the server (observed on RS 2022 and 2025 with Russian models);
        // only the names seen on a real server are mapped, any other text stays Unknown.
        private static readonly Dictionary<string, ParamDataType> DataTypes = new Dictionary<string, ParamDataType>(StringComparer.OrdinalIgnoreCase)
        {
            ["Length"] = ParamDataType.Length,
            ["Number"] = ParamDataType.Number,
            ["Material"] = ParamDataType.Material,
            ["Text"] = ParamDataType.Text,
            ["MultilineText"] = ParamDataType.MultilineText,
            ["YesNo"] = ParamDataType.YesNo,
            ["Текст"] = ParamDataType.Text,
            ["Многострочный текст"] = ParamDataType.MultilineText,
        };

        // Gets project information (project parameters).
        // Name is '@displayName', or the JSON property name (underscores shown as spaces) when the server omits it.
        // An empty or non-array response throws RevitServerApiException.
        public static async Task<List<ProjParameter>> GetProjectInfoAsync(this RevitServerApi api, string modelPath)
        {
            var encodedPath = RevitServerApi.EncodePath(modelPath);
            var command = $"{encodedPath}/projectinfo";
            var json = await api.GetAsync(command).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(json))
                throw new RevitServerApiException($"Empty response for project info: {modelPath}");
            var arr = JToken.Parse(json) as JArray;
            if (arr == null)
                throw new RevitServerApiException($"Unexpected response for project info (JSON array expected): {modelPath}");

            var result = new List<ProjParameter>();

            foreach (var obj in arr.OfType<JObject>())
            {
                string category = obj["A:title"]?.ToString();

                foreach (var prop in obj.Properties())
                {
                    if (prop.Name == "A:title") continue;
                    // Only objects are parameters; other siblings are attributes of the category
                    var param = prop.Value as JObject;
                    if (param == null) continue;
                    result.Add(new ProjParameter
                    {
                        Category = category,
                        Name = param["@displayName"]?.ToString() ?? prop.Name.Replace('_', ' ').Trim(),
                        Value = param["#text"]?.ToString(),
                        Id = param["@id"]?.ToString(),
                        Type = Enum.TryParse<ParamType>(param["@type"]?.ToString(), true, out var t) ? t : ParamType.Unknown,
                        DataType = DataTypes.TryGetValue(param["@typeOfParameter"]?.ToString() ?? string.Empty, out var dt) ? dt : ParamDataType.Unknown,
                        TypeOfParameter = param["@typeOfParameter"]?.ToString()
                    });
                }
            }
            return result;
        }
    }
}
