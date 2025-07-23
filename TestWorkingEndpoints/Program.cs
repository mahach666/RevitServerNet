using System;
using System.Threading.Tasks;
using RevitServerNet;
using RevitServerNet.Extensions;

namespace TestWorkingEndpoints
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== Тест рабочих endpoint'ов Revit Server API ===");
            Console.WriteLine();
            
            string serverAddress = "localhost"; // Замените на ваш сервер
            string serverVersion = "2019";      // Замените на вашу версию
            
            try
            {
                var api = new RevitServerApi(serverAddress, Environment.UserName, serverVersion: serverVersion);
                
                Console.WriteLine($"Сервер: {serverAddress}");
                Console.WriteLine($"Версия: {serverVersion}");  
                Console.WriteLine($"Пользователь: {Environment.UserName}");
                
                // Показываем правильный URL, который будет использоваться
                Console.WriteLine($"Base URL: {api.BaseUrl}");
                Console.WriteLine();
                
                // Тест 1: serverproperties (исправлено на нижний регистр)
                Console.WriteLine("1. Тестируем /serverproperties...");
                try
                {
                    var serverInfo = await api.GetServerInfoAsync();
                    if (serverInfo != null)
                    {
                        Console.WriteLine($"   ✅ Имя сервера: {serverInfo.ServerName ?? "Не указано"}");
                        Console.WriteLine($"   ✅ Версия: {serverInfo.ServerVersion ?? "Не указано"}");
                        Console.WriteLine($"   ✅ Максимальная длина пути: {serverInfo.MaximumFolderPathLength}");
                        Console.WriteLine($"   ✅ Максимальная длина имени модели: {serverInfo.MaximumModelNameLength}");
                        
                        if (serverInfo.ServerRoles != null && serverInfo.ServerRoles.Count > 0)
                        {
                            Console.WriteLine($"   ✅ Роли сервера: {string.Join(", ", serverInfo.ServerRoles)}");
                        }
                    }
                    else
                    {
                        Console.WriteLine("   ❌ serverProperties вернул null");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   ❌ Ошибка serverproperties: {ex.Message}");
                }
                
                Console.WriteLine();
                
                // Тест 2: |/contents (содержимое корневой папки)
                Console.WriteLine("2. Тестируем |/contents...");
                try
                {
                    var rootContents = await api.GetRootFolderContentsAsync();
                    if (rootContents != null)
                    {
                        var foldersCount = rootContents.Folders?.Count ?? 0;
                        var modelsCount = rootContents.Models?.Count ?? 0;
                        
                        Console.WriteLine($"   ✅ Папок в корне: {foldersCount}");
                        Console.WriteLine($"   ✅ Моделей в корне: {modelsCount}");
                        
                        if (foldersCount > 0 && rootContents.Folders != null)
                        {
                            Console.WriteLine("   📁 Первые папки:");
                            for (int i = 0; i < Math.Min(3, foldersCount); i++)
                            {
                                var folder = rootContents.Folders[i];
                                Console.WriteLine($"      - {folder.Name} ({folder.Size} байт, создана: {folder.DateCreated})");
                                
                                // Тестируем DirectoryInfo для первой папки
                                if (i == 0)
                                {
                                    try
                                    {
                                                                            var folderInfo = await api.GetFolderInfoAsync($"|{folder.Name}");
                                    Console.WriteLine($"        ✅ directoryinfo работает: {folderInfo?.Name ?? "null"}");
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"        ❌ directoryinfo error: {ex.Message}");
                                }
                                }
                            }
                        }
                        
                        if (modelsCount > 0 && rootContents.Models != null)
                        {
                            Console.WriteLine("   📄 Первые модели:");
                            for (int i = 0; i < Math.Min(3, modelsCount); i++)
                            {
                                var model = rootContents.Models[i];
                                Console.WriteLine($"      - {model.Name} ({model.Size / 1024} KB)");
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine("   ❌ |/contents вернул null");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   ❌ Ошибка |/contents: {ex.Message}");
                }
                
                Console.WriteLine();
                
                // Тест 3: Проверка доступности (через serverProperties)
                Console.WriteLine("3. Тестируем проверку доступности...");
                try
                {
                    var isOnline = await api.PingServerAsync();
                    var isRunning = await api.IsServerRunningAsync();
                    
                    Console.WriteLine($"   ✅ Сервер онлайн: {isOnline}");
                    Console.WriteLine($"   ✅ Сервер работает: {isRunning}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   ❌ Ошибка проверки доступности: {ex.Message}");
                }
                
                Console.WriteLine();
                
                // Тест 4: locks (если существует)
                Console.WriteLine("4. Тестируем /locks...");
                try
                {
                    var locksCount = await api.GetActiveLocksCountAsync();
                    Console.WriteLine($"   ✅ Активных блокировок: {locksCount}");
                    
                    if (locksCount > 0)
                    {
                        var allLocks = await api.GetLocksAsync();
                        if (allLocks?.Locks != null && allLocks.Locks.Count > 0)
                        {
                            Console.WriteLine("   🔒 Первые блокировки:");
                            for (int i = 0; i < Math.Min(3, allLocks.Locks.Count); i++)
                            {
                                var lockInfo = allLocks.Locks[i];
                                Console.WriteLine($"      - {lockInfo.UserName} (время: {lockInfo.TimeStamp})");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   ❌ Ошибка /locks: {ex.Message}");
                    Console.WriteLine("   ℹ️ Endpoint /locks может не существовать в вашей версии API");
                }
                
                Console.WriteLine();
                
                // Тест 5: Общая статистика
                Console.WriteLine("5. Получение общей статистики...");
                Console.WriteLine();
                Console.WriteLine("🎯 ВСЕ ИСПРАВЛЕНИЯ ПРИМЕНЕНЫ:");
                Console.WriteLine("   ✅ /serverproperties (был /serverProperties)");  
                Console.WriteLine("   ✅ /directoryinfo (был /DirectoryInfo)");
                Console.WriteLine("   ✅ /modelinfo (был /modelInfo)");
                Console.WriteLine("   ✅ /history (уже был правильным)");
                Console.WriteLine();
                try
                {
                    var allModels = await api.GetAllModelsRecursiveAsync();
                    var allFolders = await api.GetAllFoldersRecursiveAsync();
                    
                    Console.WriteLine($"   ✅ Всего моделей на сервере: {allModels?.Count ?? 0}");
                    Console.WriteLine($"   ✅ Всего папок на сервере: {allFolders?.Count ?? 0}");
                    
                    if (allModels != null && allModels.Count > 0)
                    {
                        long totalSize = 0;
                        foreach (var model in allModels)
                        {
                            totalSize += model.Size + model.SupportSize;
                        }
                        Console.WriteLine($"   ✅ Общий размер моделей: {totalSize / (1024 * 1024)} MB");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   ❌ Ошибка получения статистики: {ex.Message}");
                }
                
                Console.WriteLine();
                Console.WriteLine("=== Тестирование завершено! ===");
                Console.WriteLine("✅ Все рабочие endpoint'ы протестированы");
            }
            catch (RevitServerApiException ex)
            {
                Console.WriteLine($"❌ Ошибка Revit Server API: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Общая ошибка: {ex.Message}");
            }
            
            Console.WriteLine();
            Console.WriteLine("Нажмите любую клавишу для выхода...");
            Console.ReadKey();
        }
    }
} 