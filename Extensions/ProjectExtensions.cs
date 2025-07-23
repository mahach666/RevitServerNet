using System.Threading.Tasks;
using RevitServerNet.Models;
using System.Runtime.Serialization.Json;
using System.IO;
using System.Text;
using System.Collections.Generic;

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
        public static async Task<string> GetProjectInfoAsync(this RevitServerApi api, string modelPath)
        {
            var encodedPath = RevitServerApi.EncodePath(modelPath);
            var command = $"{encodedPath}/projectinfo"; // ДОБАВЛЕНО: отсутствовал в нашем решении
            var json = await api.GetAsync(command);
            return json; // Возвращаем raw JSON так как структура сложная
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