using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace RevitServerNet.Models
{
    // --- ENUMS ---
    public enum ServerRole { Host = 0, Accelerator = 1, Admin = 2 }
    public enum LockState { Unlocked = 0, Locked = 1, LockedParent = 2, LockedChild = 3, BeingUnlocked = 4, BeingLocked = 5 }
    public enum LockOptions { NotSet = 0, Read = 1, Write = 2, NonExclusiveReadWrite = 128, ReadAndNonExclusiveReadWrite = 129, WriteAndNonExclusiveReadWrite = 130, ReadWriteAndNonExclusiveReadWrite = 130 }
    public enum LockType { Data = 0, Permissions = 1 }
    public enum ParamType { System, Custom, Shared, Unknown }
    public enum ParamDataType { Length, Number, Material, Text, MultilineText, YesNo, Unknown }

    // --- SERVER INFO ---
    [DataContract]
    public class ServerInfo
    {
        [DataMember(Name = "Name")] public string Name { get; set; }
        [DataMember(Name = "Version")] public string Version { get; set; }
        [DataMember(Name = "MachineName")] public string MachineName { get; set; }
        [DataMember(Name = "Roles")] public List<ServerRole> Roles { get; set; }
        [DataMember(Name = "AccessLevelTypes")] public List<string> AccessLevelTypes { get; set; }
        [DataMember(Name = "MaximumFolderPathLength")] public int MaxPathLength { get; set; }
        [DataMember(Name = "MaximumModelNameLength")] public int MaxNameLength { get; set; }
        [DataMember(Name = "Servers")] public List<string> Servers { get; set; }
        // --- Для обратной совместимости ---
        [Obsolete] public string ServerName => Name;
        [Obsolete] public string ServerVersion => Version;
        [Obsolete] public List<string> ServerRoles => Roles?.ConvertAll(r => r.ToString());
        [Obsolete] public string RootPath => "/";
        [Obsolete] public int MaximumFolderPathLength => MaxPathLength;
        [Obsolete] public int MaximumModelNameLength => MaxNameLength;
    }

    // --- DRIVE INFO ---
    [DataContract]
    public class ServerDriveInfo
    {
        [DataMember(Name = "DriveSpace")] public long DriveSpace { get; set; }
        [DataMember(Name = "DriveFreeSpace")] public long DriveFreeSpace { get; set; }
    }

    // --- LOCK INFO ---
    [DataContract]
    public class IPLockInfo
    {
        [DataMember(Name = "Age")] public string Age { get; set; }
        [DataMember(Name = "LockOptions")] public LockOptions LockOptions { get; set; }
        [DataMember(Name = "LockType")] public LockType LockType { get; set; }
        [DataMember(Name = "ModelPath")] public string ModelPath { get; set; }
        [DataMember(Name = "TimeStamp")] public string TimeStamp { get; set; }
        [DataMember(Name = "UserName")] public string UserName { get; set; }
    }

    // --- FILE INFO ---
    [DataContract]
    public class RevitFileInfo
    {
        [DataMember(Name = "Path")] public string Path { get; set; }
        [DataMember(Name = "Name")] public string Name { get; set; }
        [DataMember(Name = "Size")] public long Size { get; set; }
        [DataMember(Name = "IsText")] public bool IsText { get; set; }
    }

    // --- FOLDER INFO ---
    [DataContract]
    public class FolderInfo
    {
        [DataMember(Name = "Path")] public string Path { get; set; }
        [DataMember(Name = "Name")] public string Name { get; set; }
        [DataMember(Name = "Size")] public long Size { get; set; }
        [DataMember(Name = "HasContents")] public bool HasContents { get; set; }
        [DataMember(Name = "LockContext")] public string LockContext { get; set; }
        [DataMember(Name = "LockState")] public LockState LockState { get; set; }
        [DataMember(Name = "ModelLocksInProgress")] public List<IPLockInfo> LocksInProgress { get; set; }
    }

    // --- MODEL INFO ---
    [DataContract]
    public class ModelInfo
    {
        [DataMember(Name = "Path")] public string Path { get; set; }
        [DataMember(Name = "Name")] public string Name { get; set; }
        [DataMember(Name = "Size")] public long Size { get; set; }
        [DataMember(Name = "SupportSize")] public long SupportSize { get; set; }
        [DataMember(Name = "ProductVersion")] public int ProductVersion { get; set; }
        [DataMember(Name = "LockContext")] public string LockContext { get; set; }
        [DataMember(Name = "LockState")] public LockState LockState { get; set; }
        [DataMember(Name = "ModelLocksInProgress")] public List<IPLockInfo> LocksInProgress { get; set; }
    }

    // --- EXTENDED MODEL INFO ---
    [DataContract]
    public class ModelInfoEx
    {
        [DataMember(Name = "Path")] public string Path { get; set; }
        [DataMember(Name = "Name")] public string Name { get; set; }
        [DataMember(Name = "Size")] public long Size { get; set; }
        [DataMember(Name = "Guid")] public string Guid { get; set; }
        [DataMember(Name = "DateCreated")] public string DateCreated { get; set; }
        [DataMember(Name = "DateModified")] public string DateModified { get; set; }
        [DataMember(Name = "LastModifiedBy")] public string LastModifiedBy { get; set; }
        [DataMember(Name = "SupportSize")] public long SupportSize { get; set; }
    }

    // --- ENTRY CONTENTS ---
    [DataContract]
    public class EntryContents
    {
        [DataMember(Name = "Path")] public string Path { get; set; }
        [DataMember(Name = "DriveSpace")] public long DriveSpace { get; set; }
        [DataMember(Name = "DriveFreeSpace")] public long DriveFreeSpace { get; set; }
        [DataMember(Name = "Files")] public List<RevitFileInfo> Files { get; set; }
        [DataMember(Name = "Folders")] public List<FolderInfo> Folders { get; set; }
        [DataMember(Name = "LockContext")] public string LockContext { get; set; }
        [DataMember(Name = "LockState")] public LockState LockState { get; set; }
        [DataMember(Name = "ModelLocksInProgress")] public List<IPLockInfo> LocksInProgress { get; set; }
        [DataMember(Name = "Models")] public List<ModelInfo> Models { get; set; }
    }

    // --- PROJECT INFO ---
    [DataContract]
    public class ProjectInfo
    {
        [DataMember(Name = "Parameters")] public List<ProjParameter> Parameters { get; set; }
    }

    [DataContract]
    public class ProjParameter
    {
        [DataMember(Name = "Name")] public string Name { get; set; }
        [DataMember(Name = "Value")] public string Value { get; set; }
        [DataMember(Name = "Id")] public string Id { get; set; }
        [DataMember(Name = "Category")] public string Category { get; set; }
        [DataMember(Name = "Type")] public ParamType Type { get; set; }
        [DataMember(Name = "DataType")] public ParamDataType DataType { get; set; }
    }

    // --- HISTORY ---
    [DataContract]
    public class MHistoryInfo
    {
        [DataMember(Name = "Path")] public string Path { get; set; }
        [DataMember(Name = "Items")] public List<MHistoryItemInfo> Items { get; set; }
    }

    [DataContract]
    public class MHistoryItemInfo
    {
        [DataMember(Name = "Id")] public string Id { get; set; }
        [DataMember(Name = "Comment")] public string Comment { get; set; }
        [DataMember(Name = "Date")] public string Date { get; set; }
        [DataMember(Name = "ModelSize")] public long ModelSize { get; set; }
        [DataMember(Name = "OverwrittenBy")] public string OverwrittenBy { get; set; }
        [DataMember(Name = "SupportSize")] public long SupportSize { get; set; }
        [DataMember(Name = "User")] public string User { get; set; }
    }

    // --- WALK RESULT ---
    public class WalkResult
    {
        public List<string> AllPaths { get; set; } = new List<string>();
        public List<string> FolderPaths { get; set; } = new List<string>();
        public List<string> FilePaths { get; set; } = new List<string>();
        public List<string> ModelPaths { get; set; } = new List<string>();
        public int TotalCount => AllPaths.Count;
    }

    // --- FOLDER CONTENTS (для совместимости с расширениями) ---
    [DataContract]
    public class FolderContents
    {
        [DataMember(Name = "Models")] public List<ModelInfo> Models { get; set; }
        [DataMember(Name = "Folders")] public List<FolderInfo> Folders { get; set; }
        [DataMember(Name = "Files")] public List<RevitFileInfo> Files { get; set; }
        [DataMember(Name = "Path")] public string Path { get; set; }
        [DataMember(Name = "DriveSpace")] public long DriveSpace { get; set; }
        [DataMember(Name = "DriveFreeSpace")] public long DriveFreeSpace { get; set; }
        // --- Для обратной совместимости ---
        [Obsolete] public long TotalSpace => DriveSpace;
        [Obsolete] public long FreeSpace => DriveFreeSpace;
    }

    // --- OPERATION RESULT ---
    [DataContract]
    public class OperationResult
    {
        [DataMember(Name = "Success")] public bool Success { get; set; }
        [DataMember(Name = "Message")] public string Message { get; set; }
        [DataMember(Name = "ErrorCode")] public string ErrorCode { get; set; }
    }

    // --- MODEL HISTORY (для совместимости с HistoryExtensions) ---
    [DataContract]
    public class ModelHistory
    {
        [DataMember(Name = "Items")] public List<HistoryItem> Items { get; set; }
        [DataMember(Name = "Path")] public string Path { get; set; }
    }

    [DataContract]
    public class HistoryItem
    {
        [DataMember(Name = "User")] public string User { get; set; }
        [DataMember(Name = "Date")] public string Date { get; set; }
        [DataMember(Name = "Comment")] public string Comment { get; set; }
        [DataMember(Name = "Size")] public long Size { get; set; }
        [DataMember(Name = "SupportSize")] public long SupportSize { get; set; }
        [DataMember(Name = "Version")] public int Version { get; set; }
    }

    // --- LOCK INFO (для совместимости с HistoryExtensions) ---
    [DataContract]
    public class LockInfo
    {
        [DataMember(Name = "UserName")] public string UserName { get; set; }
        [DataMember(Name = "TimeStamp")] public string TimeStamp { get; set; }
        [DataMember(Name = "ModelGUID")] public string ModelGUID { get; set; }
        [DataMember(Name = "SessionGUID")] public string SessionGUID { get; set; }
        [DataMember(Name = "TimeStampOfLastRefresh")] public string TimeStampOfLastRefresh { get; set; }
        [DataMember(Name = "DateTimeFormat")] public string DateTimeFormat { get; set; }
    }

    [DataContract]
    public class LocksList
    {
        [DataMember(Name = "Locks")] public List<LockInfo> Locks { get; set; }
    }
} 