# RevitServerNet

Библиотека .NET для работы с Autodesk Revit Server REST API. Предоставляет удобный интерфейс для взаимодействия с Revit Server, включая управление папками, моделями, историей версий и блокировками.

## Возможности

- ✅ **Полная поддержка REST API** - Все основные операции Revit Server API
- ✅ **Асинхронные методы** - Современная асинхронная архитектура
- ✅ **Расширения** - Удобные extension методы для различных сценариев
- ✅ **Типизированные модели** - Строго типизированные объекты для всех API ответов
- ✅ **Обработка ошибок** - Детальная обработка ошибок API
- ✅ **Простота использования** - Интуитивно понятный API

## Установка

1. Скомпилируйте проект:
```bash
dotnet build RevitServerNet.csproj
```

2. Подключите библиотеку к своему проекту, добавив ссылку на `RevitServerNet.dll`

## Быстрый старт

```csharp
using RevitServerNet;
using RevitServerNet.Extensions;

// Создание клиента API (имя пользователя берется из окружения)
var api = new RevitServerApi("localhost", Environment.UserName);

// Проверка доступности сервера
var isAvailable = await api.PingServerAsync();

// Получение информации о сервере
var serverInfo = await api.GetServerInfoAsync();
Console.WriteLine($"Сервер: {serverInfo.ServerName}, Версия: {serverInfo.ServerVersion}");

// Получение содержимого корневой папки
var rootContents = await api.GetRootFolderContentsAsync();
Console.WriteLine($"Папок: {rootContents.Folders.Count}, Моделей: {rootContents.Models.Count}");
```

## Основные компоненты

### RevitServerApi - Основной класс

```csharp
// Инициализация
var api = new RevitServerApi(
    host: "server-name",           // Имя сервера или IP
    userName: "Username",          // Имя пользователя
    useHttps: false,              // Использовать HTTPS (опционально)
    serverVersion: "2019"         // Версия сервера (опционально)
);

// Базовые HTTP методы
var response = await api.GetAsync("command");
var response = await api.PostAsync("command", jsonData);
var response = await api.PutAsync("command", jsonData);
var response = await api.DeleteAsync("command");
```

### ServerExtensions - Работа с сервером

```csharp
// Информация о сервере
var serverInfo = await api.GetServerInfoAsync();
var version = await api.GetServerVersionAsync();
var roles = await api.GetServerRolesAsync();

// Проверка доступности и состояния
var isOnline = await api.PingServerAsync();
var isRunning = await api.IsServerRunningAsync();
```

### FolderExtensions - Работа с папками и файлами

```csharp
// Работа с папками
var contents = await api.GetFolderContentsAsync("|MyFolder");
var folderInfo = await api.GetFolderInfoAsync("|MyFolder");
var exists = await api.FolderExistsAsync("|MyFolder");

// Управление папками
await api.CreateFolderAsync("|", "NewFolder");
await api.RenameFolderAsync("|NewFolder", "RenamedFolder");
await api.DeleteFolderAsync("|RenamedFolder");

// Работа с моделями
var modelInfo = await api.GetModelInfoAsync("|MyFolder|Model.rvt");
var modelExists = await api.ModelExistsAsync("|MyFolder|Model.rvt");

// Рекурсивный поиск
var allModels = await api.GetAllModelsRecursiveAsync();
var allFolders = await api.GetAllFoldersRecursiveAsync();
```

### HistoryExtensions - История и блокировки

```csharp
// История модели
var history = await api.GetModelHistoryAsync("|Project|Model.rvt");
var latestVersion = await api.GetLatestVersionAsync("|Project|Model.rvt");
var versionCount = await api.GetVersionCountAsync("|Project|Model.rvt");

// Участники проекта
var contributors = await api.GetModelContributorsAsync("|Project|Model.rvt");
var userVersions = await api.GetVersionsByUserAsync("|Project|Model.rvt", "UserName");

// Блокировки
var allLocks = await api.GetLocksAsync();
var isLocked = await api.IsModelLockedAsync("|Project|Model.rvt");
var lockUser = await api.GetModelLockUserAsync("|Project|Model.rvt");
var userLocks = await api.GetLocksByUserAsync("UserName");
```

## Модели данных

### ServerInfo
```csharp
public class ServerInfo
{
    public string ServerName { get; set; }
    public string ServerVersion { get; set; }
    public string RootPath { get; set; }
    public int MaximumFolderPathLength { get; set; }
    public int MaximumModelNameLength { get; set; }
    public List<string> ServerRoles { get; set; }
}
```

### FolderInfo, ModelInfo и другие модели
```csharp
public class FolderInfo
{
    public string Name { get; set; }
    public long Size { get; set; }
    public string DateCreated { get; set; }
    public string DateModified { get; set; }
    public string LockState { get; set; }
    // ... другие свойства
}

public class ModelInfo
{
    public string Name { get; set; }
    public long Size { get; set; }
    public string ModelGUID { get; set; }
    public string ProductVersion { get; set; }
    public bool IsTabular { get; set; }
    // ... другие свойства
}
```

## Обработка путей

Библиотека автоматически преобразует пути в формат Revit Server API:

```csharp
// Различные форматы путей автоматически конвертируются:
"|Folder|SubFolder|Model.rvt"     // Формат API
"/Folder/SubFolder/Model.rvt"     // Unix-стиль  
"\\Folder\\SubFolder\\Model.rvt"   // Windows-стиль
"Folder/SubFolder/Model.rvt"      // Без начального разделителя

// Все конвертируются в: |Folder|SubFolder|Model.rvt
var encodedPath = RevitServerApi.EncodePath("Folder/SubFolder/Model.rvt");
```

## Примеры использования

### Мониторинг сервера
```csharp
public async Task MonitorServerAsync()
{
    var api = new RevitServerApi("server", Environment.UserName);
    
    while (true)
    {
        var isOnline = await api.PingServerAsync();
        var isRunning = await api.IsServerRunningAsync();
        var locksCount = await api.GetActiveLocksCountAsync();
        
        Console.WriteLine($"{DateTime.Now}: Online={isOnline}, Running={isRunning}, Locks={locksCount}");
        await Task.Delay(5000); // Проверка каждые 5 секунд
    }
}
```

### Анализ проекта
```csharp
public async Task AnalyzeProjectAsync(string projectPath)
{
    var api = new RevitServerApi("server", "admin");
    
    var models = await api.GetAllModelsRecursiveAsync(projectPath);
    var totalSize = models.Sum(m => m.Size + m.SupportSize);
    
    Console.WriteLine($"Проект: {projectPath}");
    Console.WriteLine($"Моделей: {models.Count}");
    Console.WriteLine($"Общий размер: {totalSize / (1024 * 1024)} MB");
    
    foreach (var model in models)
    {
        var contributors = await api.GetModelContributorsAsync(model.Path);
        var versionCount = await api.GetVersionCountAsync(model.Path);
        
        Console.WriteLine($"  {model.Name}: {contributors.Count} участников, {versionCount} версий");
    }
}
```

### Управление папками
```csharp
public async Task OrganizeFoldersAsync()
{
    var api = new RevitServerApi("server", "admin");
    
    // Создание структуры папок для нового проекта
    await api.CreateFolderAsync("|", "Project2024");
    await api.CreateFolderAsync("|Project2024", "Architecture");
    await api.CreateFolderAsync("|Project2024", "Structure");
    await api.CreateFolderAsync("|Project2024", "MEP");
    
    // Проверка создания
    var folders = await api.GetFolderContentsAsync("|Project2024");
    Console.WriteLine($"Создано папок: {folders.Folders.Count}");
}
```

## Обработка ошибок

```csharp
try
{
    var serverInfo = await api.GetServerInfoAsync();
}
catch (RevitServerApiException ex)
{
    Console.WriteLine($"Ошибка API: {ex.Message}");
}
catch (HttpRequestException ex)
{
    Console.WriteLine($"Сетевая ошибка: {ex.Message}");
}
catch (Exception ex)
{
    Console.WriteLine($"Общая ошибка: {ex.Message}");
}
```

## Требования

- .NET Framework 4.8 или выше
- Доступ к Revit Server REST API

## Поддерживаемые версии Revit Server

- Revit Server 2019 (по умолчанию)
- Revit Server 2020
- Revit Server 2021+

Для работы с другими версиями укажите соответствующую версию при инициализации:

```csharp
var api = new RevitServerApi("server", "user", serverVersion: "2021");
```

## Лицензия

Этот проект предназначен для использования с Autodesk Revit Server. Убедитесь, что у вас есть соответствующие лицензии на использование Autodesk Revit Server.

## Участие в разработке

Для расширения функциональности или исправления ошибок:

1. Создайте форк репозитория
2. Внесите изменения
3. Добавьте тесты для новой функциональности
4. Создайте Pull Request

## Тестирование

В проекте включено тестовое консольное приложение `TestConsoleApp`, которое демонстрирует базовую функциональность:

```bash
# Сборка тестового приложения
dotnet build TestConsoleApp/TestConsoleApp.csproj

# Запуск тестов
TestConsoleApp/bin/Debug/TestConsoleApp.exe
```

**🧪 Доступные тестовые приложения:**

**🔥 TestVersions** - **ЗАПУСКАЙТЕ ПЕРВЫМ!** Проверка всех версий API:
```bash
dotnet build TestVersions/TestVersions.csproj
TestVersions\bin\Debug\TestVersions.exe
```
- 🎯 Тестирует версии 2012-2024
- 📊 Показывает какая версия работает на вашем сервере
- 💡 Рекомендует правильную версию для использования

**TestWorkingEndpoints** - проверка существующих endpoint'ов:
```bash
dotnet build TestWorkingEndpoints/TestWorkingEndpoints.csproj
TestWorkingEndpoints\bin\Debug\TestWorkingEndpoints.exe
```
- ✅ Проверяет `/serverProperties`, `/|/contents`, `/DirectoryInfo`
- 📋 Показывает содержимое сервера
- 🔍 Диагностирует проблемы с endpoint'ами

**TestConsoleApp** - базовые тесты библиотеки:
- ✅ Доступность сервера  
- ✅ Получение информации о сервере
- ✅ Содержимое корневой папки
- ✅ Общая статистика сервера

**TestDirectRequest** - прямые HTTP запросы для отладки:
```bash
# Отредактируйте server и version в коде перед запуском
TestDirectRequest\bin\Debug\TestDirectRequest.exe  
```

**⚡ Рекомендуемый порядок тестирования:**
1. **TestVersions.exe** - определите рабочую версию API
2. **TestWorkingEndpoints.exe** - проверьте endpoint'ы
3. **TestConsoleApp.exe** - полный тест библиотеки

## Важные изменения

### Версия 1.6 🔥 ПОЛНАЯ СОВМЕСТИМОСТЬ (детальное сравнение с Python кодом)

**🎯 ОКОНЧАТЕЛЬНАЯ ВЕРСИЯ:** Все методы расширений проверены и приведены в соответствие с рабочим Python кодом коллеги.

**❌ ИСПРАВЛЕНЫ КРИТИЧЕСКИЕ ОШИБКИ В ОПЕРАЦИЯХ:**
- **CreateFolder**: `POST /createFolder` → `PUT /path` ✅
- **DeleteFolder**: `/delete` → прямой путь ✅ 
- **RenameFolder**: `PUT /rename` → `DELETE ?newObjectName=` ✅

**➕ ДОБАВЛЕНЫ ОТСУТСТВУЮЩИЕ МЕТОДЫ:**
- **ProjectExtensions** - `/projectinfo` (параметры проекта модели)
- **LockingExtensions** - полный набор операций с блокировками:
  - Блокировка/разблокировка моделей
  - Отмена блокировок в процессе
  - Работа с дочерними блокировками
  - Копирование и перемещение файлов

**📊 Теперь поддерживаются ВСЕ endpoint'ы из Python API:**
```csharp
// ✅ Все основные операции
var api = new RevitServerApi("localhost", Environment.UserName, serverVersion: "2019");

// Информация и содержимое  
var info = await api.GetServerInfoAsync();           // /serverproperties
var contents = await api.GetRootFolderContentsAsync(); // /|/contents
var dirInfo = await api.GetFolderInfoAsync(path);    // /path/directoryinfo
var modelInfo = await api.GetModelInfoAsync(path);   // /path/modelinfo
var projectInfo = await api.GetProjectInfoAsync(path); // /path/projectinfo ✅ НОВОЕ

// Операции с папками/файлами
await api.CreateFolderAsync(parent, name);           // PUT /parent/name ✅ ИСПРАВЛЕНО
await api.DeleteFolderAsync(path);                   // DELETE /path ✅ ИСПРАВЛЕНО  
await api.RenameFolderAsync(path, newName);          // DELETE /path?newObjectName= ✅ ИСПРАВЛЕНО

// Блокировки и операции (НОВЫЕ)
await api.LockItemAsync(path);                       // PUT /path/lock ✅
await api.UnlockItemAsync(path);                     // DELETE /path/lock?objectMustExist=true ✅
await api.CopyItemAsync(source, dest, overwrite);    // POST с query parameters ✅
await api.MoveItemAsync(source, dest, overwrite);    // POST с query parameters ✅
```

### Версия 1.5 🔥 ПОЛНЫЕ ИСПРАВЛЕНИЯ (на основе рабочего Python кода коллеги)

**🎯 ОКОНЧАТЕЛЬНОЕ РЕШЕНИЕ:** Все ошибки найдены и исправлены благодаря анализу рабочей Python обертки коллеги в `Api/rpws-master/`.

**🔥 Финальные исправления endpoint'ов:**
- ❌ `/serverProperties` → ✅ `/serverproperties`  
- ❌ `/DirectoryInfo` → ✅ `/directoryinfo`
- ❌ `/modelInfo` → ✅ `/modelinfo`  
- ✅ `/history` был правильным

**📊 Источники анализа:**
- **server.py** - подтвердил URL версий (наши 1.4.0 исправления были верными)
- **api.py** - показал правильные endpoint'ы (**строго нижний регистр!**)
- **models.py** - детальные модели данных с типизацией
- **test.py** - полные рабочие примеры

**🧪 Тестирование:**
```bash
# Финальный тест с исправленными endpoint'ами
TestVersions\bin\Debug\TestVersions.exe
TestWorkingEndpoints\bin\Debug\TestWorkingEndpoints.exe
```

### Версия 1.4 🔥 КРИТИЧЕСКИЕ ИСПРАВЛЕНИЯ (на основе официального семпла Autodesk)

**‼️ ОСНОВНАЯ ПРОБЛЕМА:** Неправильные URL для разных версий Revit Server API.

**Источник исправлений:** Официальный семпл `RevitServerViewer` из Autodesk Revit SDK в папке `Api/RevitServerViewer/`.

**🔥 Ключевые исправления:**

1. **Правильные URL для версий API** (вместо `/RevitServerAdminRESTService{VERSION}/`):
   - **2012:** `/RevitServerAdminRESTService/AdminRESTService.svc` (без номера!)
   - **2013-2024:** `/RevitServerAdminRESTService{VERSION}/AdminRESTService.svc`

2. **Исправлен регистр endpoint'ов:** `/DirectoryInfo` вместо `/directoryInfo`

3. **Валидация версий:** Проверка поддерживаемых версий при создании API

4. **Расширена поддержка:** Добавлены версии 2020-2024

**🧪 Новые тестовые приложения:**
- **TestVersions.exe** - проверка всех версий API (2012-2024) 
- Обновлен TestWorkingEndpoints.exe с проверкой DirectoryInfo

### Версия 1.3 (исправление HTTP 405 "Method Not Allowed")

**Проблема:** Ошибка HTTP 405 при вызове несуществующих endpoint'ов `/status` и `/ping`.

**Решение:** Удалены несуществующие endpoint'ы, все методы теперь используют только реальные endpoint'ы:
- ✅ `/serverProperties` - GetServerInfoAsync()
- ✅ `/|/contents` - GetRootFolderContentsAsync()  
- ✅ `/locks` - GetLocksAsync()
- ❌ `/status` - удален (GetServerStatusAsync)
- ❌ `/ping` - удален прямой вызов

**Тестирование:** Новое приложение TestWorkingEndpoints тестирует только существующие endpoint'ы.

### Версия 1.2 (исправление "Missing or invalid User-Machine-Name")

**Проблема:** Ошибка HTTP 400 с сообщением "Missing or invalid User-Machine-Name".

**Диагностика:** Используйте тестовые приложения для проверки:
```bash
# Проверка значений заголовков
TestHeadersConsole\bin\Debug\TestHeadersConsole.exe

# Тест прямого запроса (точная копия рабочего кода)
TestDirectRequest\bin\Debug\TestDirectRequest.exe
```

**Решение:** Добавлены:
- Проверка корректности всех заголовков
- Отладочная информация для диагностики
- Тестовые приложения для проверки подключения

**Исправления в коде:**
```csharp
// Добавляем заголовки в том же порядке, что и в рабочем примере  
request.Headers.Add("User-Name", _userName);
request.Headers.Add("User-Machine-Name", Environment.MachineName);
request.Headers.Add("Operation-GUID", Guid.NewGuid().ToString());

// Отладочная информация
System.Diagnostics.Debug.WriteLine($"User-Name: '{_userName}'");
System.Diagnostics.Debug.WriteLine($"User-Machine-Name: '{Environment.MachineName}'");
```

### Версия 1.1 (первоначальное исправление HTTP 400)

**Проблема:** Первоначальная версия возвращала HTTP 400 ошибку.

**Решение:** Добавлены обязательные заголовки согласно требованиям Revit Server API:
- `User-Name` - имя пользователя
- `User-Machine-Name` - имя машины (из Environment.MachineName)
- `Operation-GUID` - уникальный GUID операции

## Примеры кода

Полные примеры использования доступны в файле `Example.cs` в проекте. 