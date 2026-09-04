using System.Collections.Generic;

namespace Satellite.DataBlockSnapshot.Handoff
{
    /// <summary>
    /// What the Add-In knows and this window does not: which project is open, which CPU
    /// the selection belongs to, and which blocks were selected.
    ///
    /// Everything here is optional. Started by hand, with nothing handed over, the window
    /// still works with the operator typing it all - and that mode is how every layer
    /// underneath was verified without TIA Portal installed.
    /// </summary>
    public sealed class SnapshotRequest
    {
        public static readonly SnapshotRequest Empty = new SnapshotRequest(null, null, null, null);

        public SnapshotRequest(
            string projectDirectory,
            string plcName,
            IReadOnlyList<string> addresses,
            IReadOnlyList<string> dataBlocks)
        {
            ProjectDirectory = projectDirectory;
            PlcName = plcName;
            Addresses = addresses ?? new string[0];
            DataBlocks = dataBlocks ?? new string[0];
        }

        /// <summary>Directory of the open TIA project, where .plc-framework lives.</summary>
        public string ProjectDirectory { get; }

        /// <summary>The device name in the project, shown so the operator can tell CPUs apart.</summary>
        public string PlcName { get; }

        /// <summary>Addresses configured on the device. A CPU may have several.</summary>
        public IReadOnlyList<string> Addresses { get; }

        public IReadOnlyList<string> DataBlocks { get; }
    }
}
