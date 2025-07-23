using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace RevitServerNet.Models
{
    /// <summary>
    /// Информация о сервере
    /// </summary>
    [DataContract]
    public class ServerInfo
    {
        [DataMember(Name = "ServerName")]
        public string ServerName { get; set; }

        [DataMember(Name = "ServerVersion")]
        public string ServerVersion { get; set; }

        [DataMember(Name = "RootPath")]
        public string RootPath { get; set; }

        [DataMember(Name = "MaximumFolderPathLength")]
        public int MaximumFolderPathLength { get; set; }

        [DataMember(Name = "MaximumModelNameLength")]
        public int MaximumModelNameLength { get; set; }

        [DataMember(Name = "ServerRoles")]
        public List<string> ServerRoles { get; set; }
    }

    /// <summary>
    /// Информация о папке
    /// </summary>
    [DataContract]
    public class FolderInfo
    {
        [DataMember(Name = "Name")]
        public string Name { get; set; }

        [DataMember(Name = "Size")]
        public long Size { get; set; }

        [DataMember(Name = "DateCreated")]
        public string DateCreated { get; set; }

        [DataMember(Name = "DateModified")]
        public string DateModified { get; set; }

        [DataMember(Name = "LockContext")]
        public string LockContext { get; set; }

        [DataMember(Name = "LockState")]
        public string LockState { get; set; }

        [DataMember(Name = "ModelLocksInFolder")]
        public int ModelLocksInFolder { get; set; }

        [DataMember(Name = "HasSubfolders")]
        public bool HasSubfolders { get; set; }

        [DataMember(Name = "Path")]
        public string Path { get; set; }
    }

    /// <summary>
    /// Содержимое папки
    /// </summary>
    [DataContract]
    public class FolderContents
    {
        [DataMember(Name = "Models")]
        public List<ModelInfo> Models { get; set; }

        [DataMember(Name = "Folders")]
        public List<FolderInfo> Folders { get; set; }

        [DataMember(Name = "Files")]
        public List<RevitFileInfo> Files { get; set; } // ДОБАВЛЕНО: из Python API

        [DataMember(Name = "Path")]
        public string Path { get; set; }

        [DataMember(Name = "DriveSpace")] 
        public long TotalSpace { get; set; } // ДОБАВЛЕНО: из Python API

        [DataMember(Name = "DriveFreeSpace")]
        public long FreeSpace { get; set; } // ДОБАВЛЕНО: из Python API
    }

    /// <summary>
    /// Информация о файле (из Python API)
    /// </summary>
    [DataContract]
    public class RevitFileInfo
    {
        [DataMember(Name = "Name")]
        public string Name { get; set; }

        [DataMember(Name = "Size")]
        public long Size { get; set; }

        [DataMember(Name = "IsText")]
        public bool IsText { get; set; }

        public string Path { get; set; } // Полный путь к файлу
    }

    /// <summary>
    /// Информация о модели
    /// </summary>
    [DataContract]
    public class ModelInfo
    {
        [DataMember(Name = "Name")]
        public string Name { get; set; }

        [DataMember(Name = "Size")]
        public long Size { get; set; }

        [DataMember(Name = "DateCreated")]
        public string DateCreated { get; set; }

        [DataMember(Name = "DateModified")]
        public string DateModified { get; set; }

        [DataMember(Name = "LockContext")]
        public string LockContext { get; set; }

        [DataMember(Name = "LockState")]
        public string LockState { get; set; }

        [DataMember(Name = "ModelGUID")]
        public string ModelGUID { get; set; }

        [DataMember(Name = "ProductVersion")]
        public string ProductVersion { get; set; }

        [DataMember(Name = "SupportSize")]
        public long SupportSize { get; set; }

        [DataMember(Name = "IsTabular")]
        public bool IsTabular { get; set; }

        [DataMember(Name = "Path")]
        public string Path { get; set; }
    }

    /// <summary>
    /// История модели
    /// </summary>
    [DataContract]
    public class ModelHistory
    {
        [DataMember(Name = "Items")]
        public List<HistoryItem> Items { get; set; }

        [DataMember(Name = "Path")]
        public string Path { get; set; }
    }

    /// <summary>
    /// Элемент истории
    /// </summary>
    [DataContract]
    public class HistoryItem
    {
        [DataMember(Name = "User")]
        public string User { get; set; }

        [DataMember(Name = "Date")]
        public string Date { get; set; }

        [DataMember(Name = "Comment")]
        public string Comment { get; set; }

        [DataMember(Name = "Size")]
        public long Size { get; set; }

        [DataMember(Name = "SupportSize")]
        public long SupportSize { get; set; }

        [DataMember(Name = "Version")]
        public int Version { get; set; }
    }

    /// <summary>
    /// Информация о блокировке
    /// </summary>
    [DataContract]
    public class LockInfo
    {
        [DataMember(Name = "UserName")]
        public string UserName { get; set; }

        [DataMember(Name = "TimeStamp")]
        public string TimeStamp { get; set; }

        [DataMember(Name = "ModelGUID")]
        public string ModelGUID { get; set; }

        [DataMember(Name = "SessionGUID")]
        public string SessionGUID { get; set; }

        [DataMember(Name = "TimeStampOfLastRefresh")]
        public string TimeStampOfLastRefresh { get; set; }

        [DataMember(Name = "DateTimeFormat")]
        public string DateTimeFormat { get; set; }
    }

    /// <summary>
    /// Список блокировок
    /// </summary>
    [DataContract]
    public class LocksList
    {
        [DataMember(Name = "Locks")]
        public List<LockInfo> Locks { get; set; }
    }

    /// <summary>
    /// Результат операции
    /// </summary>
    [DataContract]
    public class OperationResult
    {
        [DataMember(Name = "Success")]
        public bool Success { get; set; }

        [DataMember(Name = "Message")]
        public string Message { get; set; }

        [DataMember(Name = "ErrorCode")]
        public string ErrorCode { get; set; }
    }

    /// <summary>
    /// Результат обхода дерева (для WalkAsync)
    /// </summary>
    public class WalkResult
    {
        /// <summary>
        /// Все найденные пути (папки + файлы + модели)
        /// </summary>
        public List<string> AllPaths { get; set; } = new List<string>();

        /// <summary>
        /// Только пути к папкам
        /// </summary>
        public List<string> FolderPaths { get; set; } = new List<string>();

        /// <summary>
        /// Только пути к файлам
        /// </summary>
        public List<string> FilePaths { get; set; } = new List<string>();

        /// <summary>
        /// Только пути к моделям
        /// </summary>
        public List<string> ModelPaths { get; set; } = new List<string>();

        /// <summary>
        /// Общее количество найденных элементов
        /// </summary>
        public int TotalCount => AllPaths.Count;
    }
} 