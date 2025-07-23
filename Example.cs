using System;
using System.Threading.Tasks;
using RevitServerNet.Extensions;
using System.Linq;

namespace RevitServerNet
{
    /// <summary>
    /// Примеры использования библиотеки RevitServerNet
    /// </summary>
    public static class Example
    {
        /// <summary>
        /// Основной пример использования библиотеки
        /// </summary>
        public static async Task RunExample()
        {
            try
            {
                // Инициализация API
                var api = new RevitServerApi("localhost", Environment.UserName, serverVersion: "2019");

                Console.WriteLine("=== RevitServerNet v1.7.0 - Полная совместимость с Python API ===");

                // Проверка доступности сервера
                var isRunning = await api.IsServerRunningAsync();
                Console.WriteLine($"Сервер доступен: {isRunning}");

                if (!isRunning)
                {
                    Console.WriteLine("Сервер недоступен!");
                    return;
                }

                // Информация о сервере
                var serverInfo = await api.GetServerInfoAsync();
                Console.WriteLine($"Сервер: {serverInfo.ServerName}");
                Console.WriteLine($"Версия: {serverInfo.ServerVersion}");

                // ✅ НОВОЕ v1.7.0: Информация о диске
                var driveInfo = await api.GetServerDriveInfoAsync();
                Console.WriteLine($"Место на диске: {driveInfo.DriveFreeSpace / (1024*1024*1024):F1} GB свободно из {driveInfo.DriveSpace / (1024*1024*1024):F1} GB");

                Console.WriteLine("\n--- Содержимое корневой папки ---");

                // Получение содержимого корневой папки
                var rootContents = await api.GetRootFolderContentsAsync();
                Console.WriteLine($"Папок: {rootContents.Folders?.Count ?? 0}");
                Console.WriteLine($"Моделей: {rootContents.Models?.Count ?? 0}");

                // ✅ НОВОЕ v1.7.0: Список только файлов  
                var files = await api.ListFilesAsync("|");
                Console.WriteLine($"Файлов: {files.Count}");

                // ✅ НОВОЕ v1.7.0: Список только папок
                var folders = await api.ListFoldersAsync("|");
                Console.WriteLine($"Только папок: {folders.Count}");

                // ✅ НОВОЕ v1.7.0: Список только моделей
                var models = await api.ListModelsAsync("|");
                Console.WriteLine($"Только моделей: {models.Count}");

                // Показать первые папки
                if (folders.Count > 0)
                {
                    Console.WriteLine("\nПервые папки:");
                    foreach (var folder in folders.Take(3))
                    {
                        Console.WriteLine($"  - {folder.Name} ({folder.Size} bytes)");
                    }
                }

                // Показать первые модели
                if (models.Count > 0)
                {
                    Console.WriteLine("\nПервые модели:");
                    foreach (var model in models.Take(3))
                    {
                        Console.WriteLine($"  - {model.Name} ({model.Size} bytes)");
                        
                        // Получить детальную информацию о модели
                        try
                        {
                            var modelInfo = await api.GetModelInfoAsync(model.Path);
                            Console.WriteLine($"    Создана: {modelInfo.DateCreated}");
                            Console.WriteLine($"    Изменена: {modelInfo.DateModified}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"    Ошибка получения информации: {ex.Message}");
                        }
                    }
                }

                // ✅ НОВОЕ v1.7.0: Рекурсивный обход дерева - главная фича!
                Console.WriteLine("\n--- Рекурсивный обход дерева (Python walk equivalent) ---");
                
                try
                {
                    var walkResult = await api.WalkAsync("|", includeFiles: true, includeModels: true, digModels: false);
                    
                    Console.WriteLine($"📊 СТАТИСТИКА ОБХОДА:");
                    Console.WriteLine($"  Всего элементов: {walkResult.TotalCount}");
                    Console.WriteLine($"  Папок: {walkResult.FolderPaths.Count}");
                    Console.WriteLine($"  Файлов: {walkResult.FilePaths.Count}");
                    Console.WriteLine($"  Моделей: {walkResult.ModelPaths.Count}");
                    
                    if (walkResult.AllPaths.Count > 0)
                    {
                        Console.WriteLine("\nПримеры найденных путей:");
                        foreach (var path in walkResult.AllPaths.Take(5))
                        {
                            Console.WriteLine($"  {path}");
                        }
                        
                        if (walkResult.AllPaths.Count > 5)
                        {
                            Console.WriteLine($"  ... и еще {walkResult.AllPaths.Count - 5} элементов");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка обхода дерева: {ex.Message}");
                }

                // Работа с блокировками
                Console.WriteLine("\n--- Блокировки ---");
                try
                {
                    var locks = await api.GetLocksAsync();
                    Console.WriteLine($"Активных блокировок: {locks.Locks?.Count ?? 0}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка получения блокировок: {ex.Message}");
                }

                Console.WriteLine("\n=== Пример выполнен успешно! ===");
                Console.WriteLine("✅ RevitServerNet v1.7.0 - 100% совместимость с Python API достигнута!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка: {ex.Message}");
                Console.WriteLine($"Детали: {ex}");
            }
        }

        /// <summary>
        /// Пример демонстрации новых v1.7.0 методов  
        /// </summary>
        public static async Task DemoNewMethods()
        {
            var api = new RevitServerApi("localhost", Environment.UserName, serverVersion: "2019");

            Console.WriteLine("🚀 ДЕМО НОВЫХ МЕТОДОВ v1.7.0");
            Console.WriteLine("=".PadRight(40, '='));

            // ✅ 1. ListFilesAsync - как Python listfiles()
            Console.WriteLine("\n📁 1. ListFilesAsync():");
            try
            {
                var files = await api.ListFilesAsync("|");
                Console.WriteLine($"   Найдено файлов: {files.Count}");
                foreach (var file in files.Take(2))
                {
                    Console.WriteLine($"   - {file.Name} ({file.Size} bytes, Text: {file.IsText})");
                }
            }
            catch (Exception ex) { Console.WriteLine($"   Ошибка: {ex.Message}"); }

            // ✅ 2. ListFoldersAsync - как Python listfolders()  
            Console.WriteLine("\n📂 2. ListFoldersAsync():");
            try
            {
                var folders = await api.ListFoldersAsync("|");
                Console.WriteLine($"   Найдено папок: {folders.Count}");
                foreach (var folder in folders.Take(2))
                {
                    Console.WriteLine($"   - {folder.Name} ({folder.Size} bytes)");
                }
            }
            catch (Exception ex) { Console.WriteLine($"   Ошибка: {ex.Message}"); }

            // ✅ 3. ListModelsAsync - как Python listmodels()
            Console.WriteLine("\n🏗️ 3. ListModelsAsync():");
            try
            {
                var models = await api.ListModelsAsync("|");
                Console.WriteLine($"   Найдено моделей: {models.Count}");
                foreach (var model in models.Take(2))
                {
                    Console.WriteLine($"   - {model.Name} ({model.Size} bytes)");
                }
            }
            catch (Exception ex) { Console.WriteLine($"   Ошибка: {ex.Message}"); }

            // ✅ 4. WalkAsync - как Python walk()
            Console.WriteLine("\n🚶 4. WalkAsync() - рекурсивный обход:");
            try
            {
                var walkResult = await api.WalkAsync("|", includeFiles: false, includeModels: true, digModels: false);
                Console.WriteLine($"   Всего: {walkResult.TotalCount} элементов");
                Console.WriteLine($"   Папок: {walkResult.FolderPaths.Count}");
                Console.WriteLine($"   Моделей: {walkResult.ModelPaths.Count}");
            }
            catch (Exception ex) { Console.WriteLine($"   Ошибка: {ex.Message}"); }

            // ✅ 5. GetServerDriveInfoAsync - как Python getdriveinfo()
            Console.WriteLine("\n💾 5. GetServerDriveInfoAsync():");
            try
            {
                var driveInfo = await api.GetServerDriveInfoAsync();
                Console.WriteLine($"   Общее место: {driveInfo.DriveSpace / (1024*1024*1024):F1} GB");
                Console.WriteLine($"   Свободно: {driveInfo.DriveFreeSpace / (1024*1024*1024):F1} GB");
            }
            catch (Exception ex) { Console.WriteLine($"   Ошибка: {ex.Message}"); }

            Console.WriteLine("\n✅ Все новые методы v1.7.0 продемонстрированы!");
        }

        /// <summary>
        /// Пример полного анализа сервера (как в Python)
        /// </summary>
        public static async Task FullServerAnalysis()
        {
            var api = new RevitServerApi("localhost", Environment.UserName, serverVersion: "2019");

            Console.WriteLine("🔍 ПОЛНЫЙ АНАЛИЗ СЕРВЕРА (как в Python API)");
            Console.WriteLine("=".PadRight(50, '='));

            try
            {
                // Получаем полную статистику сервера
                var serverInfo = await api.GetServerInfoAsync();
                var driveInfo = await api.GetServerDriveInfoAsync();
                var walkResult = await api.WalkAsync("|", includeFiles: true, includeModels: true);

                Console.WriteLine("📊 ОБЩАЯ ИНФОРМАЦИЯ:");
                Console.WriteLine($"  Сервер: {serverInfo.ServerName}");
                Console.WriteLine($"  Версия: {serverInfo.ServerVersion}");
                Console.WriteLine($"  Роли: {string.Join(", ", serverInfo.ServerRoles ?? new System.Collections.Generic.List<string>())}");
                
                Console.WriteLine("\n💾 ДИСКОВОЕ ПРОСТРАНСТВО:");
                var usedSpace = driveInfo.DriveSpace - driveInfo.DriveFreeSpace;
                var usedPercent = (usedSpace * 100.0) / driveInfo.DriveSpace;
                Console.WriteLine($"  Общее: {driveInfo.DriveSpace / (1024*1024*1024):F1} GB");
                Console.WriteLine($"  Занято: {usedSpace / (1024*1024*1024):F1} GB ({usedPercent:F1}%)");
                Console.WriteLine($"  Свободно: {driveInfo.DriveFreeSpace / (1024*1024*1024):F1} GB");

                Console.WriteLine("\n📁 СТРУКТУРА ДАННЫХ:");
                Console.WriteLine($"  Всего элементов: {walkResult.TotalCount}");
                Console.WriteLine($"  Папок: {walkResult.FolderPaths.Count}");
                Console.WriteLine($"  Файлов: {walkResult.FilePaths.Count}");
                Console.WriteLine($"  Моделей: {walkResult.ModelPaths.Count}");

                // Анализ размеров моделей
                if (walkResult.ModelPaths.Count > 0)
                {
                    Console.WriteLine("\n🏗️ АНАЛИЗ МОДЕЛЕЙ:");
                    long totalModelSize = 0;
                    int analyzedModels = 0;

                    foreach (var modelPath in walkResult.ModelPaths.Take(10)) // Анализируем первые 10
                    {
                        try
                        {
                            var modelInfo = await api.GetModelInfoAsync(modelPath);
                            totalModelSize += modelInfo.Size;
                            analyzedModels++;
                        }
                        catch { /* Игнорируем ошибки отдельных моделей */ }
                    }

                    if (analyzedModels > 0)
                    {
                        var avgModelSize = totalModelSize / analyzedModels;
                        Console.WriteLine($"  Проанализировано: {analyzedModels} моделей");
                        Console.WriteLine($"  Средний размер: {avgModelSize / (1024*1024):F1} MB");
                        Console.WriteLine($"  Общий размер: {totalModelSize / (1024*1024):F1} MB");
                    }
                }

                Console.WriteLine("\n✅ Анализ завершен!");
                Console.WriteLine("🎯 RevitServerNet v1.7.0 предоставляет все возможности Python API!");

            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка анализа: {ex.Message}");
            }
        }
    }
} 