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
    /// <summary>
    /// Расширения для работы с проектной информацией модели
    /// </summary>
    public static class ProjectExtensions
    {
        /// <summary>
        /// Получает проектную информацию модели (параметры проекта)
        /// </summary>
        /// <param name="api">Экземпляр RevitServerApi</param>
        /// <param name="modelPath">Путь к модели</param>
        /// <returns>Проектная информация</returns>
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
        /// Десериализует JSON в объект
        /// </summary>
        /// <typeparam name="T">Тип объекта</typeparam>
        /// <param name="json">JSON строка</param>
        /// <returns>Десериализованный объект</returns>
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