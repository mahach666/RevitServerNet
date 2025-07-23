using System.Threading.Tasks;
using RevitServerNet.Models;
using System.Runtime.Serialization.Json;
using System.IO;
using System.Text;
using System.Collections.Generic;

namespace RevitServerNet.Extensions
{
    /// <summary>
    /// Расширения для работы с блокировками и locks операциями
    /// </summary>
    public static class LockingExtensions
    {
        /// <summary>
        /// Блокирует модель или папку
        /// </summary>
        /// <param name="api">Экземпляр RevitServerApi</param>
        /// <param name="itemPath">Путь к модели или папке</param>
        /// <returns>Результат операции</returns>
        public static async Task<OperationResult> LockItemAsync(this RevitServerApi api, string itemPath)
        {
            var encodedPath = RevitServerApi.EncodePath(itemPath);
            var command = $"{encodedPath}/lock";
            
            var json = await api.PutAsync(command); // Python использует PUT для lock
            return DeserializeJson<OperationResult>(json) ?? new OperationResult { Success = !string.IsNullOrEmpty(json) };
        }

        /// <summary>
        /// Разблокирует модель или папку
        /// </summary>
        /// <param name="api">Экземпляр RevitServerApi</param>
        /// <param name="itemPath">Путь к модели или папке</param>
        /// <returns>Результат операции</returns>
        public static async Task<OperationResult> UnlockItemAsync(this RevitServerApi api, string itemPath)
        {
            var encodedPath = RevitServerApi.EncodePath(itemPath);
            var command = $"{encodedPath}/lock?objectMustExist=true"; // ДОБАВЛЕНО: из Python кода
            
            var json = await api.DeleteAsync(command); // Python использует DELETE для unlock
            return DeserializeJson<OperationResult>(json) ?? new OperationResult { Success = !string.IsNullOrEmpty(json) };
        }

        /// <summary>
        /// Отменяет блокировку в процессе (cancel in-progress lock)
        /// </summary>
        /// <param name="api">Экземпляр RevitServerApi</param>
        /// <param name="itemPath">Путь к модели или папке</param>
        /// <returns>Результат операции</returns>
        public static async Task<OperationResult> CancelLockAsync(this RevitServerApi api, string itemPath)
        {
            var encodedPath = RevitServerApi.EncodePath(itemPath);
            var command = $"{encodedPath}/inProgressLock"; // ДОБАВЛЕНО: из Python кода
            
            var json = await api.DeleteAsync(command);
            return DeserializeJson<OperationResult>(json) ?? new OperationResult { Success = !string.IsNullOrEmpty(json) };
        }

        /// <summary>
        /// Получает блокировки дочерних элементов
        /// </summary>
        /// <param name="api">Экземпляр RevitServerApi</param>
        /// <param name="folderPath">Путь к папке</param>
        /// <returns>Информация о блокировках дочерних элементов</returns>
        public static async Task<string> GetDescendentLocksAsync(this RevitServerApi api, string folderPath)
        {
            var encodedPath = RevitServerApi.EncodePath(folderPath);
            var command = $"{encodedPath}/descendent/locks"; // ДОБАВЛЕНО: из Python кода
            
            var json = await api.GetAsync(command);
            return json; // Возвращаем raw JSON так как структура сложная
        }

        /// <summary>
        /// Удаляет блокировки дочерних элементов
        /// </summary>
        /// <param name="api">Экземпляр RevitServerApi</param>
        /// <param name="folderPath">Путь к папке</param>
        /// <returns>Результат операции</returns>
        public static async Task<string> DeleteDescendentLocksAsync(this RevitServerApi api, string folderPath)
        {
            var encodedPath = RevitServerApi.EncodePath(folderPath);
            var command = $"{encodedPath}/descendent/locks"; // ДОБАВЛЕНО: из Python кода
            
            var json = await api.DeleteAsync(command);
            return json; // Возвращаем raw JSON так как может содержать список неудачных операций
        }

        /// <summary>
        /// Копирует модель или папку
        /// </summary>
        /// <param name="api">Экземпляр RevitServerApi</param>
        /// <param name="sourcePath">Путь источника</param>
        /// <param name="destinationPath">Путь назначения</param>
        /// <param name="overwrite">Перезаписать если существует</param>
        /// <returns>Результат операции</returns>
        public static async Task<OperationResult> CopyItemAsync(this RevitServerApi api, string sourcePath, string destinationPath, bool overwrite = false)
        {
            var encodedSourcePath = RevitServerApi.EncodePath(sourcePath);
            var encodedDestPath = RevitServerApi.EncodePath(destinationPath);
            var command = $"{encodedSourcePath}?destinationObjectPath={encodedDestPath}&pasteAction=Copy&replaceExisting={overwrite.ToString().ToLower()}"; // ДОБАВЛЕНО: из Python кода
            
            var json = await api.PostAsync(command);
            return DeserializeJson<OperationResult>(json) ?? new OperationResult { Success = !string.IsNullOrEmpty(json) };
        }

        /// <summary>
        /// Перемещает модель или папку
        /// </summary>
        /// <param name="api">Экземпляр RevitServerApi</param>
        /// <param name="sourcePath">Путь источника</param>
        /// <param name="destinationPath">Путь назначения</param>
        /// <param name="overwrite">Перезаписать если существует</param>
        /// <returns>Результат операции</returns>
        public static async Task<OperationResult> MoveItemAsync(this RevitServerApi api, string sourcePath, string destinationPath, bool overwrite = false)
        {
            var encodedSourcePath = RevitServerApi.EncodePath(sourcePath);
            var encodedDestPath = RevitServerApi.EncodePath(destinationPath);
            var command = $"{encodedSourcePath}?destinationObjectPath={encodedDestPath}&pasteAction=Move&replaceExisting={overwrite.ToString().ToLower()}"; // ДОБАВЛЕНО: из Python кода
            
            var json = await api.PostAsync(command);
            return DeserializeJson<OperationResult>(json) ?? new OperationResult { Success = !string.IsNullOrEmpty(json) };
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