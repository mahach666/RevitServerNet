using System;
using System.IO;
using System.Net;

namespace TestDirectRequest
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("=== Тест прямого запроса к Revit Server ===");
            Console.WriteLine();
            
            string server = "localhost"; // Замените на ваш сервер
            string app_VersionNumber = "2019"; // Замените на вашу версию
            
            Console.WriteLine($"Сервер: {server}");
            Console.WriteLine($"Версия: {app_VersionNumber}");
            Console.WriteLine($"Пользователь: {Environment.UserName}");
            Console.WriteLine($"Машина: {Environment.MachineName}");
            Console.WriteLine();
            
            try
            {
                // Точная копия рабочего кода
                string stringt = $"http://{server}/RevitServerAdminRESTService{app_VersionNumber}/AdminRESTService.svc/|/contents";
                
                Console.WriteLine($"URL: {stringt}");
                Console.WriteLine();
                
                WebRequest request = WebRequest.Create(stringt);
                request.Method = "GET";
                
                // Добавляем заголовки точно как в рабочем примере
                request.Headers.Add("User-Name", Environment.UserName);
                request.Headers.Add("User-Machine-Name", Environment.MachineName);
                request.Headers.Add("Operation-GUID", Guid.NewGuid().ToString());
                
                Console.WriteLine("Отправленные заголовки:");
                Console.WriteLine($"  User-Name: {Environment.UserName}");
                Console.WriteLine($"  User-Machine-Name: {Environment.MachineName}");
                Console.WriteLine($"  Operation-GUID: [генерируется]");
                Console.WriteLine();
                
                Console.WriteLine("Выполняем запрос...");
                
                using (WebResponse response = request.GetResponse())
                using (Stream dataStream = response.GetResponseStream())
                using (StreamReader reader = new StreamReader(dataStream))
                {
                    string responseFromServer = reader.ReadToEnd();
                    
                    Console.WriteLine("✅ Запрос выполнен успешно!");
                    Console.WriteLine($"Длина ответа: {responseFromServer.Length} символов");
                    Console.WriteLine();
                    
                    if (responseFromServer.Length < 500)
                    {
                        Console.WriteLine("Ответ сервера:");
                        Console.WriteLine(responseFromServer);
                    }
                    else
                    {
                        Console.WriteLine("Начало ответа сервера:");
                        Console.WriteLine(responseFromServer.Substring(0, 500) + "...");
                    }
                }
            }
            catch (WebException ex)
            {
                Console.WriteLine($"❌ WebException: {ex.Message}");
                
                if (ex.Response != null)
                {
                    Console.WriteLine($"Статус: {((HttpWebResponse)ex.Response).StatusCode}");
                    
                    using (Stream stream = ex.Response.GetResponseStream())
                    using (StreamReader reader = new StreamReader(stream))
                    {
                        string errorResponse = reader.ReadToEnd();
                        Console.WriteLine($"Ответ сервера: {errorResponse}");
                    }
                }
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