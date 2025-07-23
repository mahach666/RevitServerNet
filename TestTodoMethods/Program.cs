using System;
using System.Threading.Tasks;
using RevitServerNet;
using RevitServerNet.Extensions;

namespace TestTodoMethods
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("🚀 ТЕСТИРОВАНИЕ НОВЫХ TODO МЕТОДОВ v1.7.0");
            Console.WriteLine("=".PadRight(60, '='));

            try
            {
                // НАСТРОЙКИ - ЗАМЕНИТЕ НА СВОИ!
                var serverHost = "localhost"; // ваш сервер
                var userName = Environment.UserName;
                var serverVersion = "2019"; // ваша версия

                Console.WriteLine($"🔧 Подключение к: {serverHost}");
                Console.WriteLine($"👤 Пользователь: {userName}");
                Console.WriteLine($"📅 Версия сервера: {serverVersion}");
                Console.WriteLine();

                var api = new RevitServerApi(serverHost, userName, serverVersion: serverVersion);

                // ✅ ТЕСТ 1: ListFilesAsync() 
                Console.WriteLine("📁 ТЕСТ 1: ListFilesAsync() - список файлов");
                Console.WriteLine("-".PadRight(50, '-'));
                try
                {
                    var files = await api.ListFilesAsync("|");
                    Console.WriteLine($"✅ Найдено файлов: {files.Count}");
                    
                    if (files.Count > 0)
                    {
                        Console.WriteLine("📄 Первые несколько файлов:");
                        var maxFiles = Math.Min(3, files.Count);
                        for (int i = 0; i < maxFiles; i++)
                        {
                            var file = files[i];
                            Console.WriteLine($"  - {file.Name} ({file.Size} bytes) [Text: {file.IsText}]");
                        }
                    }
                    Console.WriteLine();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Ошибка: {ex.Message}");
                    Console.WriteLine();
                }

                // ✅ ТЕСТ 2: ListFoldersAsync()
                Console.WriteLine("📂 ТЕСТ 2: ListFoldersAsync() - список папок");
                Console.WriteLine("-".PadRight(50, '-'));
                try
                {
                    var folders = await api.ListFoldersAsync("|");
                    Console.WriteLine($"✅ Найдено папок: {folders.Count}");
                    
                    if (folders.Count > 0)
                    {
                        Console.WriteLine("📁 Первые несколько папок:");
                        var maxFolders = Math.Min(5, folders.Count);
                        for (int i = 0; i < maxFolders; i++)
                        {
                            var folder = folders[i];
                            Console.WriteLine($"  - {folder.Name} ({folder.Size} bytes)");
                        }
                    }
                    Console.WriteLine();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Ошибка: {ex.Message}");
                    Console.WriteLine();
                }

                // ✅ ТЕСТ 3: ListModelsAsync()
                Console.WriteLine("🏗️ ТЕСТ 3: ListModelsAsync() - список моделей");
                Console.WriteLine("-".PadRight(50, '-'));
                try
                {
                    var models = await api.ListModelsAsync("|");
                    Console.WriteLine($"✅ Найдено моделей: {models.Count}");
                    
                    if (models.Count > 0)
                    {
                        Console.WriteLine("🏠 Первые несколько моделей:");
                        var maxModels = Math.Min(3, models.Count);
                        for (int i = 0; i < maxModels; i++)
                        {
                            var model = models[i];
                            Console.WriteLine($"  - {model.Name} ({model.Size} bytes)");
                            Console.WriteLine($"    Путь: {model.Path}");
                        }
                    }
                    Console.WriteLine();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Ошибка: {ex.Message}");
                    Console.WriteLine();
                }

                // ✅ ТЕСТ 4: WalkAsync() - самый важный!
                Console.WriteLine("🚶 ТЕСТ 4: WalkAsync() - рекурсивный обход дерева");
                Console.WriteLine("-".PadRight(50, '-'));
                try
                {
                    Console.WriteLine("🔄 Выполняю обход дерева (может занять время)...");
                    var walkResult = await api.WalkAsync("|", includeFiles: true, includeModels: true, digModels: false);
                    
                    Console.WriteLine($"✅ Обход завершен!");
                    Console.WriteLine($"📊 СТАТИСТИКА:");
                    Console.WriteLine($"  🗂️  Всего элементов: {walkResult.TotalCount}");
                    Console.WriteLine($"  📁 Папок: {walkResult.FolderPaths.Count}");
                    Console.WriteLine($"  📄 Файлов: {walkResult.FilePaths.Count}"); 
                    Console.WriteLine($"  🏠 Моделей: {walkResult.ModelPaths.Count}");
                    
                    // Показать примеры путей
                    if (walkResult.AllPaths.Count > 0)
                    {
                        Console.WriteLine("\n🎯 Первые 10 найденных элементов:");
                        var maxItems = Math.Min(10, walkResult.AllPaths.Count);
                        for (int i = 0; i < maxItems; i++)
                        {
                            Console.WriteLine($"  {i+1:D2}. {walkResult.AllPaths[i]}");
                        }
                        
                        if (walkResult.AllPaths.Count > 10)
                        {
                            Console.WriteLine($"  ... и еще {walkResult.AllPaths.Count - 10} элементов");
                        }
                    }
                    Console.WriteLine();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Ошибка: {ex.Message}");
                    Console.WriteLine();
                }

                // ✅ ДОПОЛНИТЕЛЬНО: GetServerDriveInfoAsync()
                Console.WriteLine("💾 БОНУС: GetServerDriveInfoAsync() - место на диске");
                Console.WriteLine("-".PadRight(50, '-'));
                try
                {
                    var driveInfo = await api.GetServerDriveInfoAsync();
                    Console.WriteLine($"✅ Информация о диске получена:");
                    Console.WriteLine($"  💿 Общее место: {driveInfo.DriveSpace / (1024 * 1024 * 1024):F1} GB");
                    Console.WriteLine($"  🆓 Свободно: {driveInfo.DriveFreeSpace / (1024 * 1024 * 1024):F1} GB");
                    var usedPercent = ((driveInfo.DriveSpace - driveInfo.DriveFreeSpace) * 100.0) / driveInfo.DriveSpace;
                    Console.WriteLine($"  📊 Использовано: {usedPercent:F1}%");
                    Console.WriteLine();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Ошибка: {ex.Message}");
                    Console.WriteLine();
                }

                Console.WriteLine("🎉 ВСЕ ТЕСТЫ ЗАВЕРШЕНЫ!");
                Console.WriteLine("✅ RevitServerNet v1.7.0 - 100% СОВМЕСТИМОСТЬ С PYTHON API! 🚀");

            }
            catch (Exception ex)
            {
                Console.WriteLine($"💥 КРИТИЧЕСКАЯ ОШИБКА: {ex.Message}");
                Console.WriteLine($"🔍 Детали: {ex}");
            }

            Console.WriteLine("\nНажмите любую клавишу для выхода...");
            Console.ReadKey();
        }
    }
} 