using System.Collections.Generic;
using System.Runtime.Serialization;

namespace RevitServerNet.Models
{
    // Models for RevitServerAdmin{YEAR}/api/... endpoints (Admin UI API).

    [DataContract]
    public class UiModelDetails
    {
        [DataMember(Name = "Id")] public string Id { get; set; }
        [DataMember(Name = "Type")] public string Type { get; set; }
        [DataMember(Name = "Name")] public string Name { get; set; }
        [DataMember(Name = "ServerPath")] public string ServerPath { get; set; }
        [DataMember(Name = "IsAlive")] public bool IsAlive { get; set; }

        [DataMember(Name = "LockStatus")] public string LockStatus { get; set; }
        [DataMember(Name = "LockContext")] public object LockContext { get; set; }

        [DataMember(Name = "Size")] public long Size { get; set; }
        [DataMember(Name = "SupportedSize")] public long SupportedSize { get; set; }
        [DataMember(Name = "ProductVersion")] public string ProductVersion { get; set; }

        [DataMember(Name = "LastUpdatedBy")] public string LastUpdatedBy { get; set; }
        [DataMember(Name = "DateCreated")] public string DateCreated { get; set; }
        [DataMember(Name = "DateModified")] public string DateModified { get; set; }
    }

    [DataContract]
    public class UiItemLockData
    {
        [DataMember(Name = "type")] public string Type { get; set; }
        [DataMember(Name = "path")] public string Path { get; set; }
        [DataMember(Name = "lockStatus")] public string LockStatus { get; set; }
        [DataMember(Name = "lockContext")] public object LockContext { get; set; }
    }

    [DataContract]
    public class UiModelHistoryItem
    {
        [DataMember(Name = "Comment")] public string Comment { get; set; }
        [DataMember(Name = "Date")] public string Date { get; set; }
        [DataMember(Name = "ModelSize")] public long ModelSize { get; set; }
        [DataMember(Name = "OverwrittenByHistoryNumber")] public int OverwrittenByHistoryNumber { get; set; }
        [DataMember(Name = "SupportSize")] public long SupportSize { get; set; }
        [DataMember(Name = "User")] public string User { get; set; }
        [DataMember(Name = "VersionNumber")] public int VersionNumber { get; set; }
    }

    [DataContract]
    public class UiTreeItem
    {
        [DataMember(Name = "Id")] public string Id { get; set; }
        [DataMember(Name = "Type")] public string Type { get; set; }
        [DataMember(Name = "Name")] public string Name { get; set; }
        [DataMember(Name = "ServerPath")] public string ServerPath { get; set; }
        [DataMember(Name = "IsAlive")] public bool IsAlive { get; set; }

        [DataMember(Name = "LockStatus")] public string LockStatus { get; set; }
        [DataMember(Name = "LockContext")] public object LockContext { get; set; }

        [DataMember(Name = "Children")] public List<UiTreeItem> Children { get; set; }
    }
}


