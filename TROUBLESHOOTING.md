# Устранение неполадок RevitServerNet

## 🔥 HTTP 405/404: "Method Not Allowed" / "Not Found" - РЕШЕНО!

### 🎯 **ОКОНЧАТЕЛЬНОЕ РЕШЕНИЕ НАЙДЕНО!**

Благодаря анализу рабочей Python обертки коллеги (`Api/rpws-master/`) найдены и исправлены **ВСЕ критические ошибки**:

**❌ БЫЛО (неправильно):**
```
/serverProperties  ← CamelCase НЕ РАБОТАЕТ!
/DirectoryInfo     ← CamelCase НЕ РАБОТАЕТ!  
/modelInfo         ← CamelCase НЕ РАБОТАЕТ!
```

**✅ СТАЛО (правильно):**
```  
/serverproperties  ← строго нижний регистр ✅
/directoryinfo     ← строго нижний регистр ✅
/modelinfo         ← строго нижний регистр ✅
/history          ← уже был правильным ✅
```

### 🔍 Источник исправлений
**Python код коллеги** `Api/rpws-master/rpws/api.py`:
```python
REQ_CMD_SERVERPROP = "/serverproperties"    # НЕ /serverProperties!
REQ_CMD_DIRINFO = "/directoryinfo"          # НЕ /DirectoryInfo!
REQ_CMD_MODELINFO = "/modelinfo"            # НЕ /modelInfo!
```

**Вывод:** Revit Server API требует **строго нижний регистр** всех endpoint'ов!

### 🚨 Симптомы
```
RevitServerNet.RevitServerApiException: API request failed with status MethodNotAllowed
RevitServerNet.RevitServerApiException: API request failed with status NotFound (404)
Внутреннее исключение: WebException: The remote server returned an error: (405) Method Not Allowed.
```

### 🔍 Основные причины
1. **Неправильный URL для версии API** - **САМАЯ ЧАСТАЯ ПРОБЛЕМА!**
2. API пытается обратиться к несуществующему endpoint'у
3. Неправильный регистр букв в endpoint'ах

### ✅ Решение 1: Правильные версии API (КРИТИЧЕСКИ ВАЖНО)

**❌ СТАРЫЕ НЕПРАВИЛЬНЫЕ URL:**
```
http://server/RevitServerAdminRESTService2019/AdminRESTService.svc  # НЕПРАВИЛЬНО!
```

**✅ ПРАВИЛЬНЫЕ URL (из официального семпла Autodesk):**
```
2012: http://server/RevitServerAdminRESTService/AdminRESTService.svc      # Без номера!
2013: http://server/RevitServerAdminRESTService2013/AdminRESTService.svc
2014: http://server/RevitServerAdminRESTService2014/AdminRESTService.svc
...
2024: http://server/RevitServerAdminRESTService2024/AdminRESTService.svc
```

**Новая версия библиотеки (1.4+) автоматически использует правильные URL!**

### ✅ Решение 2: Используйте только рабочие endpoint'ы
- ✅ `/serverProperties` - информация о сервере  
- ✅ `/|/contents` - содержимое корневой папки
- ✅ `/DirectoryInfo` - информация о папке (заглавная D!)
- ✅ `/locks` - блокировки (может не работать в старых версиях)
- ❌ `/status` - НЕ СУЩЕСТВУЕТ  
- ❌ `/ping` - НЕ СУЩЕСТВУЕТ

### 🧪 Диагностика версий
```bash
# Тест всех версий API для вашего сервера
TestVersions\bin\Debug\TestVersions.exe

# Тест рабочих endpoint'ов
TestWorkingEndpoints\bin\Debug\TestWorkingEndpoints.exe
```

### 💡 Рекомендации
1. **Всегда запускайте TestVersions.exe первым** для определения правильной версии
2. Используйте версию, которая показала ✅ в тестах
3. Если все версии показывают 404/405 - проверьте настройки Revit Server

## HTTP 400: "Missing or invalid User-Machine-Name"

### 🚨 Симптомы
```
RevitServerNet.RevitServerApiException: API request failed with status BadRequest: 
Внутреннее исключение: WebException: The remote server returned an error: (400) Missing or invalid User-Machine-Name.
```

### 🔍 Диагностика

#### Шаг 1: Проверьте значения заголовков
```bash
# Запустите тест заголовков
TestHeadersConsole\bin\Debug\TestHeadersConsole.exe
```

Убедитесь что:
- `Environment.UserName` не пустое
- `Environment.MachineName` не пустое  
- Нет специальных символов в именах

#### Шаг 2: Проверьте прямое подключение
```bash
# Запустите тест прямого запроса
TestDirectRequest\bin\Debug\TestDirectRequest.exe
```

Измените в коде:
- `string server = "your-server-address";` - ваш адрес сервера
- `string app_VersionNumber = "2019";` - вашу версию сервера

#### Шаг 3: Проверьте отладочный вывод
В Visual Studio включите Debug Output для просмотра отладочной информации:
- View → Output → Show output from: Debug

### ✅ Решения

#### Решение 1: Использование правильного конструктора
```csharp
// ❌ Неправильно - жестко заданное имя пользователя
var api = new RevitServerApi("server", "Administrator");

// ✅ Правильно - имя текущего пользователя
var api = new RevitServerApi("server", Environment.UserName);
```

#### Решение 2: Проверьте адрес и версию сервера
```csharp
// Убедитесь что используете правильные параметры
var api = new RevitServerApi(
    host: "your-server-ip-or-name",     // НЕ localhost если сервер удаленный
    userName: Environment.UserName,
    useHttps: false,                    // true если используется HTTPS
    serverVersion: "2019"               // или ваша версия: 2020, 2021, etc.
);
```

#### Решение 3: Проверьте сетевое подключение
```csharp
// Тест доступности сервера
try 
{
    var ping = new System.Net.NetworkInformation.Ping();
    var reply = ping.Send("your-server-address");
    Console.WriteLine($"Ping: {reply.Status}");
}
catch (Exception ex)
{
    Console.WriteLine($"Сервер недоступен: {ex.Message}");
}
```

## HTTP 400: Общие ошибки

### Неправильный URL
```csharp
// ❌ Неправильно - отсутствует версия или неправильный формат
"http://server/RevitServerAdminRESTService/AdminRESTService.svc/|/contents"

// ✅ Правильно - с указанием версии
"http://server/RevitServerAdminRESTService2019/AdminRESTService.svc/|/contents"
```

### Отсутствуют обязательные заголовки
Убедитесь что добавляются ВСЕ три заголовка:
```csharp
request.Headers.Add("User-Name", Environment.UserName);
request.Headers.Add("User-Machine-Name", Environment.MachineName);  
request.Headers.Add("Operation-GUID", Guid.NewGuid().ToString());
```

### Неправильное кодирование пути
```csharp
// ❌ Неправильно - слэши
"/folder/subfolder/model.rvt"

// ✅ Правильно - вертикальные черты  
"|folder|subfolder|model.rvt"

// Используйте метод кодирования
var encodedPath = RevitServerApi.EncodePath("/folder/subfolder/model.rvt");
```

## Сетевые ошибки

### Таймаут подключения
```csharp
// Увеличьте таймаут для медленных соединений
var api = new RevitServerApi("server", Environment.UserName);

// В будущих версиях можно будет настраивать таймаут
// request.Timeout = 30000; // 30 секунд
```

### Проблемы с прокси
```csharp
// Если используется корпоративный прокси
WebRequest.DefaultWebProxy = WebRequest.GetSystemWebProxy();
WebRequest.DefaultWebProxy.Credentials = CredentialCache.DefaultCredentials;
```

## Версии сервера

| Версия Revit | URL компонент |
|--------------|---------------|
| 2019 | RevitServerAdminRESTService2019 |
| 2020 | RevitServerAdminRESTService2020 |  
| 2021 | RevitServerAdminRESTService2021 |
| 2022+ | RevitServerAdminRESTService |

## Получение дополнительной помощи

1. **Включите отладку** - все HTTP запросы логируются в Debug Output
2. **Запустите тестовые приложения** - они покажут точную причину ошибки
3. **Проверьте логи Revit Server** - на сервере могут быть дополнительные детали
4. **Убедитесь в доступности сервера** - ping и telnet к порту 80/443

### Контрольный список
- [ ] Сервер доступен по сети  
- [ ] Правильно указана версия сервера
- [ ] Environment.UserName и MachineName не пустые
- [ ] Нет специальных символов в именах пользователя/машины
- [ ] Используется правильный формат путей (| вместо / или \)
- [ ] Все три заголовка добавляются корректно 