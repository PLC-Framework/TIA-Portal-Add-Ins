using System.Collections.Generic;

namespace AddIn.Shared.Actions
{
    /// <summary>
    /// What the version-specific adapter found out about the selection, reduced to
    /// primitives before it crosses into this layer.
    ///
    /// A satellite cannot ask TIA anything, so everything it needs has to be gathered
    /// here and handed over: which CPU, how to reach it, and which blocks were picked.
    /// </summary>
    public sealed class PlcSelection
    {
        public PlcSelection(string plcName, IReadOnlyList<string> addresses, IReadOnlyList<string> dataBlocks)
        {
            PlcName = plcName;
            Addresses = addresses ?? new string[0];
            DataBlocks = dataBlocks ?? new string[0];
        }

        /// <summary>The device name in the project, so the operator can tell CPUs apart.</summary>
        public string PlcName { get; }

        /// <summary>
        /// Every address configured on the device. A CPU has more than one often enough
        /// that picking the first for the operator would be wrong as often as right.
        /// </summary>
        public IReadOnlyList<string> Addresses { get; }

        public IReadOnlyList<string> DataBlocks { get; }
    }
}
