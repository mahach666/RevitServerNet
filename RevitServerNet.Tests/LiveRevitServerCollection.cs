using Xunit;

namespace RevitServerNet.Tests
{
    /// <summary>
    /// All live Revit Server test classes: they run one after another and not in parallel with any other test. They share one server:
    /// a lock or a folder operation of one class changes what another one reads (the LockState of the root and of parent folders,
    /// 403 answers in a locked parent), and the exports hold the read lock of the shared model.
    /// </summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class LiveRevitServerCollection
    {
        public const string Name = "Live Revit Server";
    }
}
