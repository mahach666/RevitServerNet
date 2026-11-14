using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System.Xml;

namespace RevitServerNet.Models
{
    // --- ENUMS ---
    public enum ServerRole { Host = 0, Accelerator = 1, Admin = 2 }
    public enum LockState { Unlocked = 0, Locked = 1, LockedParent = 2, LockedChild = 3, BeingUnlocked = 4, BeingLocked = 5 }
    public enum LockOptions { NotSet = 0, Read = 1, Write = 2, NonExclusiveReadWrite = 128, ReadAndNonExclusiveReadWrite = 129, WriteAndNonExclusiveReadWrite = 130, ReadWriteAndNonExclusiveReadWrite = 130 }
    public enum LockType { Data = 0, Permissions = 1 }
    public enum ParamType { System, Custom, Shared, Unknown }
    public enum ParamDataType { Length, Number, Material, Text, MultilineText, YesNo, Unknown }

    /// <summary>
    /// Dates of the REST service are WCF strings like "/Date(1780826100000)/" or "/Date(1780826100000+0300)/"
    /// (milliseconds since the Unix epoch, UTC; the offset is informational).
    /// </summary>
    internal static class WcfDate
    {
        private static readonly Regex Pattern = new Regex(@"^/Date\((-?\d+)([+-]\d{4})?\)/$", RegexOptions.CultureInvariant);

        public static DateTime? ParseUtc(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            var m = Pattern.Match(value);
            if (!m.Success || !long.TryParse(m.Groups[1].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var ms))
                return null;
            try { return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(ms); }
            catch (ArgumentOutOfRangeException) { return null; }
        }
    }

    internal static class PathNames
    {
        /// <summary>Last segment of a server path as returned by the REST service ("Folder\\Model.rvt") or a pipe path.</summary>
        public static string LastSegment(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            return path.Split('\\', '|', '/').LastOrDefault(x => x.Length > 0);
        }
    }

    // --- SERVER INFO ---
    [DataContract]
    public class ServerInfo
    {
        // The server does not return a name or a version in serverproperties.
        [DataMember(Name = "Name")] public string Name { get; set; }
        /// <summary>Not sent by the server; GetServerInfoAsync fills it with the year of the client's configured server version.</summary>
        [DataMember(Name = "Version")] public string Version { get; set; }
        [DataMember(Name = "MachineName")] public string MachineName { get; set; }
        [DataMember(Name = "ServerRoles")] public List<ServerRole> Roles { get; set; }
        [DataMember(Name = "AccessLevelTypes")] public List<string> AccessLevelTypes { get; set; }
        [DataMember(Name = "MaximumFolderPathLength")] public int MaxPathLength { get; set; }
        [DataMember(Name = "MaximumModelNameLength")] public int MaxNameLength { get; set; }
        [DataMember(Name = "Servers")] public List<string> Servers { get; set; }
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
        [DataMember(Name = "ModelLockOptions")] public LockOptions LockOptions { get; set; }
        [DataMember(Name = "ModelLockType")] public LockType LockType { get; set; }
        [DataMember(Name = "ModelPath")] public string ModelPath { get; set; }
        [DataMember(Name = "TimeStamp")] public string TimeStamp { get; set; }
        [DataMember(Name = "UserName")] public string UserName { get; set; }
        /// <summary><see cref="TimeStamp"/> as UTC, null when absent or unparsable.</summary>
        public DateTime? TimeStampUtc => WcfDate.ParseUtc(TimeStamp);
        /// <summary><see cref="Age"/> (ISO-8601 duration like "PT1.0150588S"), null when absent or unparsable.</summary>
        public TimeSpan? AgeSpan
        {
            get
            {
                if (string.IsNullOrEmpty(Age)) return null;
                try { return XmlConvert.ToTimeSpan(Age); }
                catch (FormatException) { return null; }
                catch (OverflowException) { return null; }
            }
        }
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
        private string _name;
        private bool? _hasContents;

        [DataMember(Name = "Path")] public string Path { get; set; }
        /// <summary>The server sends Name in a parent's contents only; for directoryinfo it is taken from <see cref="Path"/>.</summary>
        [DataMember(Name = "Name")] public string Name { get => _name ?? PathNames.LastSegment(Path); set => _name = value; }
        [DataMember(Name = "Size")] public long Size { get; set; }
        /// <summary>The server sends HasContents in a parent's contents only; for directoryinfo it is derived from <see cref="ModelCount"/> and <see cref="FolderCount"/>.</summary>
        [DataMember(Name = "HasContents")] public bool HasContents { get => _hasContents ?? (ModelCount + FolderCount > 0); set => _hasContents = value; }
        [DataMember(Name = "ModelCount")] public int ModelCount { get; set; }
        [DataMember(Name = "FolderCount")] public int FolderCount { get; set; }
        [DataMember(Name = "ModelSize")] public long ModelSize { get; set; }
        [DataMember(Name = "Exists")] public bool? Exists { get; set; } // only in directoryinfo; null for folders listed in contents
        [DataMember(Name = "IsFolder")] public bool IsFolder { get; set; }
        [DataMember(Name = "DateCreated")] public string DateCreated { get; set; }
        [DataMember(Name = "DateModified")] public string DateModified { get; set; }
        [DataMember(Name = "LastModifiedBy")] public string LastModifiedBy { get; set; }
        public DateTime? DateCreatedUtc => WcfDate.ParseUtc(DateCreated);
        public DateTime? DateModifiedUtc => WcfDate.ParseUtc(DateModified);
        [DataMember(Name = "LockContext")] public string LockContext { get; set; }
        [DataMember(Name = "LockState")] public LockState LockState { get; set; }
        [DataMember(Name = "ModelLocksInProgress")] public List<IPLockInfo> LocksInProgress { get; set; }
    }

    // --- MODEL INFO ---
    [DataContract]
    public class ModelInfo
    {
        private string _name;

        [DataMember(Name = "Path")] public string Path { get; set; }
        /// <summary>The server sends Name in a folder's contents only; for modelinfo it is taken from <see cref="Path"/>.</summary>
        [DataMember(Name = "Name")] public string Name { get => _name ?? PathNames.LastSegment(Path); set => _name = value; }
        /// <summary>Size of the model file ("ModelSize" on the wire).</summary>
        [DataMember(Name = "ModelSize")] public long Size { get; set; }
        [DataMember(Name = "SupportSize")] public long SupportSize { get; set; }
        [DataMember(Name = "ModelGUID")] public string ModelGUID { get; set; }
        [DataMember(Name = "DateCreated")] public string DateCreated { get; set; }
        [DataMember(Name = "DateModified")] public string DateModified { get; set; }
        [DataMember(Name = "LastModifiedBy")] public string LastModifiedBy { get; set; }
        public DateTime? DateCreatedUtc => WcfDate.ParseUtc(DateCreated);
        public DateTime? DateModifiedUtc => WcfDate.ParseUtc(DateModified);
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
        [DataMember(Name = "ModelSize")] public long Size { get; set; }
        [DataMember(Name = "ModelGUID")] public string Guid { get; set; }
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
        // '@typeOfParameter' as the server sent it (localized by the server; DataType is Unknown for names not mapped)
        [DataMember(Name = "TypeOfParameter")] public string TypeOfParameter { get; set; }
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
        [DataMember(Name = "OverwrittenByHistoryNumber")] public int OverwrittenByHistoryNumber { get; set; }
        [DataMember(Name = "VersionNumber")] public int VersionNumber { get; set; }
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

    // --- FOLDER CONTENTS (for compatibility with extensions) ---
    [DataContract]
    public class FolderContents
    {
        [DataMember(Name = "Models")] public List<ModelInfo> Models { get; set; }
        [DataMember(Name = "Folders")] public List<FolderInfo> Folders { get; set; }
        [DataMember(Name = "Files")] public List<RevitFileInfo> Files { get; set; }
        [DataMember(Name = "Path")] public string Path { get; set; }
        [DataMember(Name = "DriveSpace")] public long DriveSpace { get; set; }
        [DataMember(Name = "DriveFreeSpace")] public long DriveFreeSpace { get; set; }
        [DataMember(Name = "LockContext")] public string LockContext { get; set; }
        [DataMember(Name = "LockState")] public LockState LockState { get; set; }
        [DataMember(Name = "ModelLocksInProgress")] public List<IPLockInfo> LocksInProgress { get; set; }
        // --- For backward compatibility ---
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

    // --- MODEL HISTORY (for compatibility with HistoryExtensions) ---
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
        /// <summary>Size of the model file of this version ("ModelSize" on the wire).</summary>
        [DataMember(Name = "ModelSize")] public long Size { get; set; }
        [DataMember(Name = "SupportSize")] public long SupportSize { get; set; }
        /// <summary>Version number ("VersionNumber" on the wire).</summary>
        [DataMember(Name = "VersionNumber")] public int Version { get; set; }
        [DataMember(Name = "OverwrittenByHistoryNumber")] public int OverwrittenByHistoryNumber { get; set; }
        /// <summary><see cref="Date"/> as UTC, null when absent or unparsable.</summary>
        public DateTime? DateUtc => WcfDate.ParseUtc(Date);
    }

    // --- LOCK INFO (for compatibility with HistoryExtensions) ---
    [DataContract]
    public class LockInfo
    {
        [DataMember(Name = "UserName")] public string UserName { get; set; }
        [DataMember(Name = "TimeStamp")] public string TimeStamp { get; set; }
        [DataMember(Name = "ModelGUID")] public string ModelGUID { get; set; }
        [DataMember(Name = "SessionGUID")] public string SessionGUID { get; set; }
        [DataMember(Name = "TimeStampOfLastRefresh")] public string TimeStampOfLastRefresh { get; set; }
        [DataMember(Name = "DateTimeFormat")] public string DateTimeFormat { get; set; }
        /// <summary>Path of the locked model or folder as the server reports it ("Folder\\Model.rvt"). Filled by the lock queries.</summary>
        public string ModelPath { get; set; }
        /// <summary>Lock state of <see cref="ModelPath"/>. Filled by the lock queries.</summary>
        public LockState LockState { get; set; }
    }

    [DataContract]
    public class LocksList
    {
        [DataMember(Name = "Locks")] public List<LockInfo> Locks { get; set; }
    }
} 