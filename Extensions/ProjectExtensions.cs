using System.Threading.Tasks;
using RevitServerNet.Models;
using System.Runtime.Serialization.Json;
using System.IO;
using System.Text;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using System;

namespace RevitServerNet.Extensions
{
    // Extensions for working with project information
    public static class ProjectExtensions
    {
        // Gets project information (project parameters)
        public static async Task<List<ProjParameter>> GetProjectInfoAsync(this RevitServerApi api, string modelPath)
        {
            var encodedPath = RevitServerApi.EncodePath(modelPath);
            var command = $"{encodedPath}/projectinfo";
            var json = await api.GetAsync(command);

            var result = new List<ProjParameter>();
            var arr = JArray.Parse(json);

            foreach (var item in arr)
            {
                string category = item["A:title"]?.ToString();

                foreach (var prop in item.Children<JProperty>())
                {
                    if (prop.Name == "A:title") continue;
                    var param = prop.Value;
                    result.Add(new ProjParameter
                    {
                        Category = category,
                        Name = param["@displayName"]?.ToString(),
                        Value = param["#text"]?.ToString(),
                        Id = param["@id"]?.ToString(),
                        Type = Enum.TryParse<ParamType>(param["@type"]?.ToString(), true, out var t) ? t : ParamType.Unknown,
                        DataType = Enum.TryParse<ParamDataType>(param["@typeOfParameter"]?.ToString(), true, out var dt) ? dt : ParamDataType.Unknown
                    });
                }
            }
            return result;
        }

        /// <summary>
        /// Deserializes JSON to an object
        /// </summary>
        /// <typeparam name="T">Object type</typeparam>
        /// <param name="json">JSON string</param>
        /// <returns>Deserialized object</returns>
        private static T DeserializeJson<T>(string json) where T : class
        {
            if (string.IsNullOrEmpty(json))
                return null;

            var serializer = new DataContractJsonSerializer(typeof(T));
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                return serializer.ReadObject(stream) as T;
            }
        }
    }
} 