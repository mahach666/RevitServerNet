using System;
using System.Threading.Tasks;
using RevitServerNet;
using RevitServerNet.Extensions;

namespace TestConsoleApp
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== Тест RevitServerNet API ===");
            
            // Замените "localhost" на адрес вашего Revit Server
            string serverAddress = "localhost";
            
            try
            {
                // Создание клиента API
                var api = new RevitServerApi(serverAddress, Environment.UserName);
                
                Console.WriteLine($"Подключение к серверу: {serverAddress}");
                Console.WriteLine($"Пользователь: {Environment.UserName}");
                Console.WriteLine($"Машина: {Environment.MachineName}");
                Console.WriteLine();
                
                // Тест 1: Проверка доступности сервера
                Console.WriteLine("1. Проверка доступности сервера...");
                var isServerAvailable = await api.PingServerAsync();
                Console.WriteLine($"   Результат: {(isServerAvailable ? "✅ Сервер доступен" : "❌ Сервер недоступен")}");
                
                if (!isServerAvailable)
                {
                    Console.WriteLine("Сервер недоступен. Проверьте адрес и убедитесь, что Revit Server запущен.");
                    return;
                }
                
                Console.WriteLine();
                
                // Тест 2: Получение информации о сервере
                Console.WriteLine("2. Получение информации о сервере...");
                try
                {
                    var serverInfo = await api.GetServerInfoAsync();
                    if (serverInfo != null)
                    {
                        Console.WriteLine($"   ✅ Имя сервера: {serverInfo.ServerName ?? "Не указано"}");
                        Console.WriteLine($"   ✅ Версия: {serverInfo.ServerVersion ?? "Не указано"}");
                        Console.WriteLine($"   ✅ Корневой путь: {serverInfo.RootPath ?? "Не указано"}");
                    }
                    else
                    {
                        Console.WriteLine("   ⚠️ Информация о сервере недоступна");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   ❌ Ошибка получения информации о сервере: {ex.Message}");
                }
                
                Console.WriteLine();
                
                // Тест 3: Получение содержимого корневой папки
                Console.WriteLine("3. Получение содержимого корневой папки...");
                try
                {
                    var rootContents = await api.GetRootFolderContentsAsync();
                    if (rootContents != null)
                    {
                        var foldersCount = rootContents.Folders?.Count ?? 0;
                        var modelsCount = rootContents.Models?.Count ?? 0;
                        
                        Console.WriteLine($"   ✅ Папок в корне: {foldersCount}");
                        Console.WriteLine($"   ✅ Моделей в корне: {modelsCount}");
                        
                        if (rootContents.Folders != null && foldersCount > 0)
                        {
                            Console.WriteLine("   📁 Папки:");
                            foreach (var folder in rootContents.Folders)
                            {
                                Console.WriteLine($"      - {folder.Name} ({folder.Size} байт)");
                            }
                        }
                        
                        if (rootContents.Models != null && modelsCount > 0)
                        {
                            Console.WriteLine("   📄 Модели:");
                            foreach (var model in rootContents.Models)
                            {
                                Console.WriteLine($"      - {model.Name} ({model.Size / 1024} KB, версия {model.ProductVersion ?? "неизвестна"})");
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine("   ⚠️ Содержимое корневой папки недоступно");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   ❌ Ошибка получения содержимого: {ex.Message}");
                }
                
                Console.WriteLine();
                
                // Тест 4: Получение статистики сервера
                Console.WriteLine("4. Получение общей статистики...");
                try
                {
                    var allModels = await api.GetAllModelsRecursiveAsync();
                    var allFolders = await api.GetAllFoldersRecursiveAsync();
                    var activeLocksCount = await api.GetActiveLocksCountAsync();
                    
                    Console.WriteLine($"   ✅ Всего моделей на сервере: {allModels?.Count ?? 0}");
                    Console.WriteLine($"   ✅ Всего папок на сервере: {allFolders?.Count ?? 0}");
                    Console.WriteLine($"   ✅ Активных блокировок: {activeLocksCount}");
                    
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
                Console.WriteLine("=== Тестирование завершено успешно! ===");
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