using System;

namespace TestHeadersConsole
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("=== Проверка заголовков для Revit Server API ===");
            Console.WriteLine();
            
            // Проверяем значения Environment
            Console.WriteLine("Проверяем значения окружения:");
            
            var userName = Environment.UserName;
            var machineName = Environment.MachineName;
            var operationGuid = Guid.NewGuid().ToString();
            
            Console.WriteLine($"Environment.UserName: '{userName}' (длина: {userName?.Length ?? 0})");
            Console.WriteLine($"Environment.MachineName: '{machineName}' (длина: {machineName?.Length ?? 0})");
            Console.WriteLine($"Operation-GUID: '{operationGuid}' (длина: {operationGuid.Length})");
            Console.WriteLine();
            
            // Проверяем на пустые значения
            Console.WriteLine("Проверка на пустые значения:");
            Console.WriteLine($"UserName пустое: {string.IsNullOrEmpty(userName)}");
            Console.WriteLine($"MachineName пустое: {string.IsNullOrEmpty(machineName)}");
            Console.WriteLine($"GUID пустое: {string.IsNullOrEmpty(operationGuid)}");
            Console.WriteLine();
            
            // Проверяем символы в именах
            Console.WriteLine("Проверка символов:");
            if (!string.IsNullOrEmpty(userName))
            {
                Console.WriteLine($"UserName содержит специальные символы: {ContainsSpecialChars(userName)}");
                Console.WriteLine($"UserName байты: {string.Join(", ", System.Text.Encoding.UTF8.GetBytes(userName))}");
            }
            
            if (!string.IsNullOrEmpty(machineName))
            {
                Console.WriteLine($"MachineName содержит специальные символы: {ContainsSpecialChars(machineName)}");
                Console.WriteLine($"MachineName байты: {string.Join(", ", System.Text.Encoding.UTF8.GetBytes(machineName))}");
            }
            Console.WriteLine();
            
            // Тест создания HTTP заголовков
            Console.WriteLine("Тест создания HTTP заголовков:");
            try
            {
                var request = System.Net.WebRequest.Create("http://test.com");
                request.Headers.Add("User-Name", userName ?? "NULL");
                request.Headers.Add("User-Machine-Name", machineName ?? "NULL");
                request.Headers.Add("Operation-GUID", operationGuid);
                
                Console.WriteLine("✅ Заголовки успешно добавлены");
                Console.WriteLine($"User-Name: {request.Headers["User-Name"]}");
                Console.WriteLine($"User-Machine-Name: {request.Headers["User-Machine-Name"]}");
                Console.WriteLine($"Operation-GUID: {request.Headers["Operation-GUID"]}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка при добавлении заголовков: {ex.Message}");
            }
            
            Console.WriteLine();
            Console.WriteLine("Нажмите любую клавишу для выхода...");
            Console.ReadKey();
        }
        
        static bool ContainsSpecialChars(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            
            foreach (char c in text)
            {
                if (c < 32 || c > 126) // Не ASCII печатаемые символы
                    return true;
            }
            return false;
        }
    }
} 