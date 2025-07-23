using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace RevitServerNet
{
    /// <summary>
    /// Основной класс для работы с Revit Server REST API
    /// </summary>
    public class RevitServerApi
    {
        private readonly string _baseUrl;
        private readonly string _userName;
        
        // Официальные пути API для разных версий (из семпла Autodesk)
        private static readonly Dictionary<string, string> SupportedVersions = new Dictionary<string, string>
        {
            {"2012", "/RevitServerAdminRESTService/AdminRESTService.svc"},
            {"2013", "/RevitServerAdminRESTService2013/AdminRESTService.svc"},
            {"2014", "/RevitServerAdminRESTService2014/AdminRESTService.svc"},
            {"2015", "/RevitServerAdminRESTService2015/AdminRESTService.svc"},
            {"2016", "/RevitServerAdminRESTService2016/AdminRESTService.svc"},
            {"2017", "/RevitServerAdminRESTService2017/AdminRESTService.svc"},
            {"2018", "/RevitServerAdminRESTService2018/AdminRESTService.svc"},
            {"2019", "/RevitServerAdminRESTService2019/AdminRESTService.svc"},
            {"2020", "/RevitServerAdminRESTService2020/AdminRESTService.svc"},
            {"2021", "/RevitServerAdminRESTService2021/AdminRESTService.svc"},
            {"2022", "/RevitServerAdminRESTService2022/AdminRESTService.svc"},
            {"2023", "/RevitServerAdminRESTService2023/AdminRESTService.svc"},
            {"2024", "/RevitServerAdminRESTService2024/AdminRESTService.svc"},
            {"2025", "/RevitServerAdminRESTService2025/AdminRESTService.svc"},
            {"2026", "/RevitServerAdminRESTService2026/AdminRESTService.svc"},
        };

        /// <summary>
        /// Инициализирует новый экземпляр RevitServerApi
        /// </summary>
        /// <param name="host">Хост Revit Server (например: "localhost" или IP адрес)</param>
        /// <param name="userName">Имя пользователя для API запросов</param>
        /// <param name="useHttps">Использовать HTTPS (по умолчанию false)</param>
        /// <param name="serverVersion">Версия сервера (по умолчанию "2019")</param>
        public RevitServerApi(string host, string userName, bool useHttps = false, string serverVersion = "2019")
        {
            if (string.IsNullOrEmpty(host))
                throw new ArgumentException("Host cannot be null or empty", nameof(host));
            if (string.IsNullOrEmpty(userName))
                throw new ArgumentException("UserName cannot be null or empty", nameof(userName));
            
            if (!SupportedVersions.ContainsKey(serverVersion))
                throw new ArgumentException($"Unsupported server version '{serverVersion}'. Supported versions: {string.Join(", ", SupportedVersions.Keys)}", nameof(serverVersion));

            var protocol = useHttps ? "https" : "http";
            var servicePath = SupportedVersions[serverVersion];
            _baseUrl = $"{protocol}://{host}{servicePath}";
            _userName = userName;
        }

        /// <summary>
        /// Базовый URL для API
        /// </summary>
        public string BaseUrl => _baseUrl;

        /// <summary>
        /// Имя пользователя
        /// </summary>
        public string UserName => _userName;

        /// <summary>
        /// Выполняет GET запрос к API
        /// </summary>
        /// <param name="command">Команда API</param>
        /// <param name="additionalHeaders">Дополнительные заголовки</param>
        /// <returns>JSON ответ от сервера</returns>
        public async Task<string> GetAsync(string command, Dictionary<string, string> additionalHeaders = null)
        {
            return await ExecuteRequestAsync("GET", command, additionalHeaders);
        }

        /// <summary>
        /// Выполняет POST запрос к API
        /// </summary>
        /// <param name="command">Команда API</param>
        /// <param name="data">Данные для отправки</param>
        /// <param name="additionalHeaders">Дополнительные заголовки</param>
        /// <returns>JSON ответ от сервера</returns>
        public async Task<string> PostAsync(string command, string data = null, Dictionary<string, string> additionalHeaders = null)
        {
            return await ExecuteRequestAsync("POST", command, additionalHeaders, data);
        }

        /// <summary>
        /// Выполняет PUT запрос к API
        /// </summary>
        /// <param name="command">Команда API</param>
        /// <param name="data">Данные для отправки</param>
        /// <param name="additionalHeaders">Дополнительные заголовки</param>
        /// <returns>JSON ответ от сервера</returns>
        public async Task<string> PutAsync(string command, string data = null, Dictionary<string, string> additionalHeaders = null)
        {
            return await ExecuteRequestAsync("PUT", command, additionalHeaders, data);
        }

        /// <summary>
        /// Выполняет DELETE запрос к API
        /// </summary>
        /// <param name="command">Команда API</param>
        /// <param name="additionalHeaders">Дополнительные заголовки</param>
        /// <returns>JSON ответ от сервера</returns>
        public async Task<string> DeleteAsync(string command, Dictionary<string, string> additionalHeaders = null)
        {
            return await ExecuteRequestAsync("DELETE", command, additionalHeaders);
        }

        /// <summary>
        /// Получает корректное имя машины для Revit Server API
        /// </summary>
        /// <returns>Корректное имя машины</returns>
        private string GetValidMachineName()
        {
            var machineName = Environment.MachineName;
            
            // Если имя машины пустое, используем альтернативные способы
            if (string.IsNullOrWhiteSpace(machineName))
            {
                machineName = Environment.GetEnvironmentVariable("COMPUTERNAME") ?? "UNKNOWN";
            }
            
            // Удаляем недопустимые символы и ограничиваем длину
            machineName = System.Text.RegularExpressions.Regex.Replace(machineName, @"[^\w\-]", "");
            if (machineName.Length > 50) // Ограничиваем длину
            {
                machineName = machineName.Substring(0, 50);
            }
            
            // Если после очистки имя пустое, используем значение по умолчанию
            if (string.IsNullOrWhiteSpace(machineName))
            {
                machineName = "CLIENT-PC";
            }
            
            return machineName;
        }

        /// <summary>
        /// Кодирует путь в формат API (заменяет разделители на |)
        /// </summary>
        /// <param name="path">Путь к файлу или папке</param>
        /// <returns>Кодированный путь</returns>
        public static string EncodePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return "|"; // Корневая папка

            // Заменяем различные разделители на |
            path = path.Replace('\\', '|').Replace('/', '|');
            
            // Убираем двойные разделители
            while (path.Contains("||"))
                path = path.Replace("||", "|");

            // Добавляем | в начало если его нет
            if (!path.StartsWith("|"))
                path = "|" + path;

            return path;
        }

        /// <summary>
        /// Выполняет HTTP запрос к API
        /// </summary>
        /// <param name="method">HTTP метод</param>
        /// <param name="command">Команда API</param>
        /// <param name="additionalHeaders">Дополнительные заголовки</param>
        /// <param name="data">Данные для отправки</param>
        /// <returns>JSON ответ от сервера</returns>
        private async Task<string> ExecuteRequestAsync(string method, string command, Dictionary<string, string> additionalHeaders = null, string data = null)
        {
            var url = $"{_baseUrl}/{command}";
            var request = WebRequest.Create(url) as HttpWebRequest;
            
            // Отладочная информация
            System.Diagnostics.Debug.WriteLine($"=== Revit Server API Request ===");
            System.Diagnostics.Debug.WriteLine($"URL: {url}");
            System.Diagnostics.Debug.WriteLine($"Method: {method}");
            
            if (request == null)
                throw new InvalidOperationException("Cannot create HTTP request");

            request.Method = method;
            
            // Устанавливаем ContentType только для POST/PUT запросов с данными
            if (!string.IsNullOrEmpty(data) && (method == "POST" || method == "PUT"))
            {
                request.ContentType = "application/json";
            }
            
            // Добавляем обязательные заголовки для Revit Server API
            // Используем точно такие же заголовки, как в рабочем примере
            var machineName = Environment.MachineName;
            var operationGuid = Guid.NewGuid().ToString();
            
            // Проверяем что все обязательные значения не пустые
            if (string.IsNullOrEmpty(_userName))
                throw new InvalidOperationException("User name cannot be null or empty");
            if (string.IsNullOrEmpty(machineName))
                throw new InvalidOperationException("Machine name cannot be null or empty");
            
            // Добавляем заголовки в том же порядке, что и в рабочем примере
            request.Headers.Add("User-Name", _userName);
            request.Headers.Add("User-Machine-Name", machineName);
            request.Headers.Add("Operation-GUID", operationGuid);
            
            // Отладочная информация о заголовках
            System.Diagnostics.Debug.WriteLine($"User-Name: '{_userName}'");
            System.Diagnostics.Debug.WriteLine($"User-Machine-Name: '{machineName}'");
            System.Diagnostics.Debug.WriteLine($"Operation-GUID: '{operationGuid}'");
            System.Diagnostics.Debug.WriteLine($"=== End Headers ===");
            
            // Добавляем дополнительные заголовки
            if (additionalHeaders != null)
            {
                foreach (var header in additionalHeaders)
                {
                    request.Headers.Add(header.Key, header.Value);
                }
            }

            // Отправляем данные если есть
            if (!string.IsNullOrEmpty(data) && (method == "POST" || method == "PUT"))
            {
                var bytes = Encoding.UTF8.GetBytes(data);
                request.ContentLength = bytes.Length;
                
                using (var stream = await request.GetRequestStreamAsync())
                {
                    await stream.WriteAsync(bytes, 0, bytes.Length);
                }
            }

            try
            {
                using (var response = await request.GetResponseAsync() as HttpWebResponse)
                using (var stream = response?.GetResponseStream())
                {
                    if (stream == null)
                        return null;

                    using (var reader = new StreamReader(stream))
                    {
                        return await reader.ReadToEndAsync();
                    }
                }
            }
            catch (WebException ex)
            {
                if (ex.Response is HttpWebResponse errorResponse)
                {
                    using (var stream = errorResponse.GetResponseStream())
                    using (var reader = new StreamReader(stream))
                    {
                        var errorContent = await reader.ReadToEndAsync();
                        throw new RevitServerApiException($"API request failed with status {errorResponse.StatusCode}: {errorContent}", ex);
                    }
                }
                throw new RevitServerApiException("API request failed", ex);
            }
        }
    }

    /// <summary>
    /// Исключение для ошибок API Revit Server
    /// </summary>
    public class RevitServerApiException : Exception
    {
        public RevitServerApiException(string message) : base(message) { }
        public RevitServerApiException(string message, Exception innerException) : base(message, innerException) { }
    }
}
