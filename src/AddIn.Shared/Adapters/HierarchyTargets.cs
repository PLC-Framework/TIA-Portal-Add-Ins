using System.Collections.Generic;

namespace AddIn.Shared.Adapters
{
    /// <summary>
    /// The four group roots a PLC exposes, as ports. The version-specific Add-In fills
    /// this in from the selected device; the action never learns which Siemens types are
    /// behind them.
    ///
    /// Any of them may be null when that family is not available.
    /// </summary>
    public sealed class HierarchyTargets
    {
        public IGroupNode Blocks { get; set; }
        public IGroupNode TechnologyObjects { get; set; }
        public IGroupNode TagTables { get; set; }
        public IGroupNode Types { get; set; }

        /// <summary>
        /// One entry per existing software unit. Empty on an S7-1200, which has none.
        /// The Add-In never creates units: it only fills the ones already there.
        /// </summary>
        public List<HierarchySoftwareUnitTargets> SoftwareUnits { get; set; }
    }
}
