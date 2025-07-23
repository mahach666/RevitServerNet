using System;
using System.Threading.Tasks;
using RevitServerNet;
using RevitServerNet.Extensions;

namespace TestVersions
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== Тест поддержки разных версий Revit Server API ===");
            Console.WriteLine();
            
            string serverAddress = "localhost"; // Замените на ваш сервер
            
            // Список версий для тестирования
            string[] versionsToTest = { "2012", "2013", "2014", "2015", "2016", "2017", "2018", "2019", "2020", "2021", "2022", "2023", "2024" };
            
            Console.WriteLine($"Сервер: {serverAddress}");
            Console.WriteLine($"Пользователь: {Environment.UserName}");
            Console.WriteLine();
            
            foreach (string version in versionsToTest)
            {
                Console.WriteLine($"🔧 Тестируем версию {version}...");
                
                try
                {
                    var api = new RevitServerApi(serverAddress, Environment.UserName, serverVersion: version);
                    Console.WriteLine($"   ✅ URL создан: {api.BaseUrl}");
                    
                    // Пробуем простой запрос
                    try
                    {
                        var serverInfo = await api.GetServerInfoAsync();
                        if (serverInfo != null)
                        {
                            Console.WriteLine($"   🎯 РАБОТАЕТ! Сервер: {serverInfo.ServerName}, Версия API: {serverInfo.ServerVersion}");
                        }
                        else
                        {
                            Console.WriteLine($"   ⚠️ Запрос прошел, но данные не получены");
                        }
                    }
                    catch (RevitServerApiException apiEx)
                    {
                        // HTTP ошибки от сервера
                        if (apiEx.Message.Contains("404") || apiEx.Message.Contains("NotFound"))
                        {
                            Console.WriteLine($"   ❌ Версия {version} не найдена на сервере (404)");
                        }
                        else if (apiEx.Message.Contains("405") || apiEx.Message.Contains("MethodNotAllowed"))
                        {
                            Console.WriteLine($"   ❌ Версия {version}: метод не разрешен (405)");
                        }
                        else
                        {
                            Console.WriteLine($"   ❌ Ошибка API для версии {version}: {apiEx.Message}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"   ❌ Общая ошибка для версии {version}: {ex.Message}");
                    }
                }
                catch (ArgumentException argEx)
                {
                    // Ошибка неподдерживаемой версии
                    Console.WriteLine($"   ❌ Версия {version} не поддерживается библиотекой: {argEx.Message}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   ❌ Критическая ошибка для версии {version}: {ex.Message}");
                }
                
                Console.WriteLine();
                
                // Небольшая пауза между версиями
                await Task.Delay(500);
            }
            
            Console.WriteLine("=== Тестирование версий завершено! ===");
            Console.WriteLine();
            Console.WriteLine("📋 Результаты показывают:");
            Console.WriteLine("   ✅ - Версия работает");
            Console.WriteLine("   ❌ 404 - Версия не установлена на сервере");
            Console.WriteLine("   ❌ 405 - Неправильный endpoint или метод");
            Console.WriteLine("   ❌ API/Общая - Проблема с конфигурацией");
            Console.WriteLine();
            Console.WriteLine("💡 Рекомендация: Используйте версию, которая показала ✅ результат");
            Console.WriteLine();
            Console.WriteLine("Нажмите любую клавишу для выхода...");
            Console.ReadKey();
        }
    }
} 