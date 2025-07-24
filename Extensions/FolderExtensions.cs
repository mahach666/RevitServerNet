using System.Threading.Tasks;
using RevitServerNet.Models;
using System.Collections.Generic;
using System.Diagnostics;
using Newtonsoft.Json;

namespace RevitServerNet.Extensions
{
    /// <summary>
    /// Расширения для работы с папками и файлами
    /// </summary>
    public static class FolderExtensions
    {
        /// <summary>
        /// Получает содержимое корневой папки
        /// </summary>
        /// <param name="api">Экземпляр RevitServerApi</param>
        /// <returns>Содержимое корневой папки</returns>
        public static async Task<FolderContents> GetRootFolderContentsAsync(this RevitServerApi api)
        {
            return await GetFolderContentsAsync(api, "|");
        }

        /// <summary>
        /// Получает содержимое папки по пути
        /// </summary>
        /// <param name="api">Экземпляр RevitServerApi</param>
        /// <param name="folderPath">Путь к папке</param>
        /// <returns>Содержимое папки</returns>
        public static async Task<FolderContents> GetFolderContentsAsync(this RevitServerApi api, string folderPath)
        {
            var encodedPath = RevitServerApi.EncodePath(folderPath);
            var command = $"{encodedPath}/contents";
            var json = await api.GetAsync(command);
            System.Diagnostics.Debug.WriteLine($"[GetFolderContentsAsync] Path: {folderPath} | Encoded: {encodedPath} | JSON: {json}");
            return DeserializeJson<FolderContents>(json);
        }

        /// <summary>
        /// Получает информацию о папке (DirectoryInfo - официальный endpoint из семпла Autodesk)
        /// </summary>
        /// <param name="api">Экземпляр RevitServerApi</param>
        /// <param name="folderPath">Путь к папке</param>
        /// <returns>Информация о папке</returns>
        public static async Task<FolderInfo> GetFolderInfoAsync(this RevitServerApi api, string folderPath)
        {
            var encodedPath = RevitServerApi.EncodePath(folderPath);
            var command = $"{encodedPath}/directoryinfo"; // ИСПРАВЛЕНО: нижний регистр как в Python коде коллеги
            var json = await api.GetAsync(command);
            return DeserializeJson<FolderInfo>(json);
        }

        /// <summary>
        /// Создает новую папку
        /// </summary>
        /// <param name="api">Экземпляр RevitServerApi</param>
        /// <param name="parentFolderPath">Путь к родительской папке</param>
        /// <param name="folderName">Имя новой папки</param>
        /// <returns>Результат операции</returns>
        public static async Task<OperationResult> CreateFolderAsync(this RevitServerApi api, string parentFolderPath, string folderName)
        {
            var encodedPath = RevitServerApi.EncodePath($"{parentFolderPath}/{folderName}");
            var command = $"{encodedPath}"; // ИСПРАВЛЕНО: Python использует пустую строку + PUT запрос
            
            var json = await api.PutAsync(command);
            return DeserializeJson<OperationResult>(json) ?? new OperationResult { Success = !string.IsNullOrEmpty(json) };
        }

        /// <summary>
        /// Удаляет папку
        /// </summary>
        /// <param name="api">Экземпляр RevitServerApi</param>
        /// <param name="folderPath">Путь к папке</param>
        /// <returns>Результат операции</returns>
        public static async Task<OperationResult> DeleteFolderAsync(this RevitServerApi api, string folderPath)
        {
            var encodedPath = RevitServerApi.EncodePath(folderPath);
            var command = $"{encodedPath}"; // ИСПРАВЛЕНО: Python использует только path + DELETE запрос
            
            var json = await api.DeleteAsync(command);
            return DeserializeJson<OperationResult>(json) ?? new OperationResult { Success = !string.IsNullOrEmpty(json) };
        }

        /// <summary>
        /// Переименовывает папку
        /// </summary>
        /// <param name="api">Экземпляр RevitServerApi</param>
        /// <param name="folderPath">Путь к папке</param>
        /// <param name="newName">Новое имя папки</param>
        /// <returns>Результат операции</returns>
        public static async Task<OperationResult> RenameFolderAsync(this RevitServerApi api, string folderPath, string newName)
        {
            var encodedPath = RevitServerApi.EncodePath(folderPath);
            var command = $"{encodedPath}?newObjectName={newName}"; // ИСПРАВЛЕНО: Python использует query parameter
            
            var json = await api.DeleteAsync(command); // ИСПРАВЛЕНО: Python использует DELETE для rename
            return DeserializeJson<OperationResult>(json) ?? new OperationResult { Success = !string.IsNullOrEmpty(json) };
        }

        /// <summary>
        /// Получает информацию о модели
        /// </summary>
        /// <param name="api">Экземпляр RevitServerApi</param>
        /// <param name="modelPath">Путь к модели</param>
        /// <returns>Информация о модели</returns>
        public static async Task<ModelInfo> GetModelInfoAsync(this RevitServerApi api, string modelPath)
        {
            var encodedPath = RevitServerApi.EncodePath(modelPath);
            var command = $"{encodedPath}/modelinfo"; // ИСПРАВЛЕНО: нижний регистр как в Python коде коллеги
            var json = await api.GetAsync(command);
            return DeserializeJson<ModelInfo>(json);
        }

        /// <summary>
        /// Получает размер папки
        /// </summary>
        /// <param name="api">Экземпляр RevitServerApi</param>
        /// <param name="folderPath">Путь к папке</param>
        /// <returns>Размер папки в байтах</returns>
        public static async Task<long> GetFolderSizeAsync(this RevitServerApi api, string folderPath)
        {
            var folderInfo = await GetFolderInfoAsync(api, folderPath);
            return folderInfo?.Size ?? 0;
        }

        /// <summary>
        /// Проверяет существование папки
        /// </summary>
        /// <param name="api">Экземпляр RevitServerApi</param>
        /// <param name="folderPath">Путь к папке</param>
        /// <returns>True если папка существует</returns>
        public static async Task<bool> FolderExistsAsync(this RevitServerApi api, string folderPath)
        {
            try
            {
                await GetFolderInfoAsync(api, folderPath);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Проверяет существование модели
        /// </summary>
        /// <param name="api">Экземпляр RevitServerApi</param>
        /// <param name="modelPath">Путь к модели</param>
        /// <returns>True если модель существует</returns>
        public static async Task<bool> ModelExistsAsync(this RevitServerApi api, string modelPath)
        {
            try
            {
                await GetModelInfoAsync(api, modelPath);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Получает список всех моделей в папке (включая подпапки)
        /// </summary>
        /// <param name="api">Экземпляр RevitServerApi</param>
        /// <param name="folderPath">Путь к папке</param>
        /// <returns>Список моделей</returns>
        public static async Task<List<ModelInfo>> GetAllModelsRecursiveAsync(this RevitServerApi api, string folderPath = "|")
        {
            var models = new List<ModelInfo>();
            var visited = new HashSet<string>();
            Debug.WriteLine($"[GetAllModelsRecursiveAsync] Start: {folderPath}");
            await GetModelsRecursiveInternal(api, folderPath, models, visited);
            Debug.WriteLine($"[GetAllModelsRecursiveAsync] Done. Total models: {models.Count}");
            return models;
        }

        /// <summary>
        /// Получает список всех папок (включая подпапки)
        /// </summary>
        /// <param name="api">Экземпляр RevitServerApi</param>
        /// <param name="folderPath">Путь к папке</param>
        /// <returns>Список папок</returns>
        public static async Task<List<FolderInfo>> GetAllFoldersRecursiveAsync(this RevitServerApi api, string folderPath = "|")
        {
            var folders = new List<FolderInfo>();
            var visited = new HashSet<string>();
            Debug.WriteLine($"[GetAllFoldersRecursiveAsync] Start: {folderPath}");
            await GetFoldersRecursiveInternal(api, folderPath, folders, visited);
            Debug.WriteLine($"[GetAllFoldersRecursiveAsync] Done. Total folders: {folders.Count}");
            return folders;
        }

        /// <summary>
        /// Рекурсивно получает модели
        /// </summary>
        private static async Task GetModelsRecursiveInternal(RevitServerApi api, string folderPath, List<ModelInfo> models, HashSet<string> visited)
        {
            if (string.IsNullOrEmpty(folderPath) || visited.Contains(folderPath))
            {
                Debug.WriteLine($"[GetModelsRecursiveInternal] Skip: {folderPath}");
                return;
            }
            visited.Add(folderPath);
            Debug.WriteLine($"[GetModelsRecursiveInternal] Enter: {folderPath}");
            var contents = await GetFolderContentsAsync(api, folderPath);
            if (contents == null)
            {
                Debug.WriteLine($"[GetModelsRecursiveInternal] Null contents: {folderPath}");
                return;
            }
            if (contents.Models != null)
            {
                Debug.WriteLine($"[GetModelsRecursiveInternal] Models found: {contents.Models.Count} in {folderPath}");
                models.AddRange(contents.Models);
            }
            if (contents.Folders != null)
            {
                Debug.WriteLine($"[GetModelsRecursiveInternal] Folders found: {contents.Folders.Count} in {folderPath}");
                foreach (var folder in contents.Folders)
                {
                    var childPath = folderPath == "|" ? "|" + folder.Name : folderPath + "|" + folder.Name;
                    if (!string.IsNullOrEmpty(childPath) && childPath != folderPath)
                    {
                        Debug.WriteLine($"[GetModelsRecursiveInternal] Recurse: {childPath}");
                        await GetModelsRecursiveInternal(api, childPath, models, visited);
                    }
                }
            }
            Debug.WriteLine($"[GetModelsRecursiveInternal] Exit: {folderPath}");
        }

        /// <summary>
        /// Рекурсивно получает папки
        /// </summary>
        private static async Task GetFoldersRecursiveInternal(RevitServerApi api, string folderPath, List<FolderInfo> folders, HashSet<string> visited)
        {
            if (string.IsNullOrEmpty(folderPath) || visited.Contains(folderPath))
            {
                Debug.WriteLine($"[GetFoldersRecursiveInternal] Skip: {folderPath}");
                return;
            }
            visited.Add(folderPath);
            Debug.WriteLine($"[GetFoldersRecursiveInternal] Enter: {folderPath}");
            var contents = await GetFolderContentsAsync(api, folderPath);
            if (contents?.Folders == null)
            {
                Debug.WriteLine($"[GetFoldersRecursiveInternal] Null or no folders: {folderPath}");
                return;
            }
            folders.AddRange(contents.Folders);
            Debug.WriteLine($"[GetFoldersRecursiveInternal] Folders found: {contents.Folders.Count} in {folderPath}");
            foreach (var folder in contents.Folders)
            {
                var childPath = folderPath == "|" ? "|" + folder.Name : folderPath + "|" + folder.Name;
                if (!string.IsNullOrEmpty(childPath) && childPath != folderPath)
                {
                    Debug.WriteLine($"[GetFoldersRecursiveInternal] Recurse: {childPath}");
                    await GetFoldersRecursiveInternal(api, childPath, folders, visited);
                }
            }
            Debug.WriteLine($"[GetFoldersRecursiveInternal] Exit: {folderPath}");
        }

        /// <summary>
        /// Получает список только файлов из папки (как в Python listfiles)
        /// </summary>
        /// <param name="api">Экземпляр RevitServerApi</param>
        /// <param name="folderPath">Путь к папке (по умолчанию корневая)</param>
        /// <returns>Список файлов</returns>
        public static async Task<List<RevitFileInfo>> ListFilesAsync(this RevitServerApi api, string folderPath = "|")
        {
            var contents = await GetFolderContentsAsync(api, folderPath);
            return contents?.Files ?? new List<RevitFileInfo>();
        }

        /// <summary>
        /// Получает список только папок из папки (как в Python listfolders)  
        /// </summary>
        /// <param name="api">Экземпляр RevitServerApi</param>
        /// <param name="folderPath">Путь к папке (по умолчанию корневая)</param>
        /// <returns>Список папок</returns>
        public static async Task<List<FolderInfo>> ListFoldersAsync(this RevitServerApi api, string folderPath = "|")
        {
            var contents = await GetFolderContentsAsync(api, folderPath);
            return contents?.Folders ?? new List<FolderInfo>();
        }

        /// <summary>
        /// Получает список только моделей из папки (как в Python listmodels)
        /// </summary>
        /// <param name="api">Экземпляр RevitServerApi</param>
        /// <param name="folderPath">Путь к папке (по умолчанию корневая)</param>
        /// <returns>Список моделей</returns>
        public static async Task<List<ModelInfo>> ListModelsAsync(this RevitServerApi api, string folderPath = "|")
        {
            var contents = await GetFolderContentsAsync(api, folderPath);
            return contents?.Models ?? new List<ModelInfo>();
        }

        /// <summary>
        /// Рекурсивный обход дерева папок (как в Python walk)
        /// Возвращает все пути в дереве папок
        /// </summary>
        /// <param name="api">Экземпляр RevitServerApi</param>
        /// <param name="topPath">Начальная папка (по умолчанию корневая)</param>
        /// <param name="includeFiles">Включать файлы в результат</param>
        /// <param name="includeModels">Включать модели в результат</param>
        /// <param name="digModels">Обходить содержимое внутри моделей</param>
        /// <returns>Список всех найденных путей</returns>
        public static async Task<WalkResult> WalkAsync(this RevitServerApi api, string topPath = "|", bool includeFiles = true, bool includeModels = true, bool digModels = false)
        {
            var result = new WalkResult();
            var visited = new HashSet<string>();
            Debug.WriteLine($"[WalkAsync] Start: {topPath}");
            await WalkRecursiveInternal(api, topPath, includeFiles, includeModels, digModels, result, visited);
            Debug.WriteLine($"[WalkAsync] Done. Total: {result.TotalCount}");
            return result;
        }

        /// <summary>
        /// Внутренняя рекурсивная функция для обхода дерева
        /// </summary>
        private static async Task WalkRecursiveInternal(RevitServerApi api, string currentPath, bool includeFiles, bool includeModels, bool digModels, WalkResult result, HashSet<string> visited)
        {
            if (string.IsNullOrEmpty(currentPath) || visited.Contains(currentPath))
            {
                Debug.WriteLine($"[WalkRecursiveInternal] Skip: {currentPath}");
                return;
            }
            visited.Add(currentPath);
            Debug.WriteLine($"[WalkRecursiveInternal] Enter: {currentPath}");
            try
            {
                var contents = await GetFolderContentsAsync(api, currentPath);
                if (contents == null)
                {
                    Debug.WriteLine($"[WalkRecursiveInternal] Null contents: {currentPath}");
                    return;
                }
                result.AllPaths.Add(currentPath);
                result.FolderPaths.Add(currentPath);
                if (includeFiles && contents.Files != null)
                {
                    Debug.WriteLine($"[WalkRecursiveInternal] Files found: {contents.Files.Count} in {currentPath}");
                    foreach (var file in contents.Files)
                    {
                        var filePath = currentPath == "|" ? "|" + file.Name : currentPath + "|" + file.Name;
                        Debug.WriteLine($"[WalkRecursiveInternal] File: {filePath}");
                        result.AllPaths.Add(filePath);
                        result.FilePaths.Add(filePath);
                    }
                }
                if (includeModels && contents.Models != null)
                {
                    Debug.WriteLine($"[WalkRecursiveInternal] Models found: {contents.Models.Count} in {currentPath}");
                    foreach (var model in contents.Models)
                    {
                        var modelPath = currentPath == "|" ? "|" + model.Name : currentPath + "|" + model.Name;
                        Debug.WriteLine($"[WalkRecursiveInternal] Model: {modelPath}");
                        result.AllPaths.Add(modelPath);
                        result.ModelPaths.Add(modelPath);
                    }
                }
                if (contents.Folders != null)
                {
                    Debug.WriteLine($"[WalkRecursiveInternal] Folders found: {contents.Folders.Count} in {currentPath}");
                    foreach (var folder in contents.Folders)
                    {
                        var childPath = currentPath == "|" ? "|" + folder.Name : currentPath + "|" + folder.Name;
                        if (!string.IsNullOrEmpty(childPath) && childPath != currentPath)
                        {
                            Debug.WriteLine($"[WalkRecursiveInternal] Recurse: {childPath}");
                            await WalkRecursiveInternal(api, childPath, includeFiles, includeModels, digModels, result, visited);
                        }
                    }
                }
                if (digModels && includeModels && contents.Models != null)
                {
                    foreach (var model in contents.Models)
                    {
                        var modelPath = currentPath == "|" ? "|" + model.Name : currentPath + "|" + model.Name;
                        if (!string.IsNullOrEmpty(modelPath) && modelPath != currentPath)
                        {
                            Debug.WriteLine($"[WalkRecursiveInternal] DigModel: {modelPath}");
                            await WalkRecursiveInternal(api, modelPath, includeFiles, includeModels, digModels, result, visited);
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.WriteLine($"[WalkRecursiveInternal] ERROR in {currentPath}: {ex.Message}");
            }
            Debug.WriteLine($"[WalkRecursiveInternal] Exit: {currentPath}");
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
            return JsonConvert.DeserializeObject<T>(json);
        }
    }
} 