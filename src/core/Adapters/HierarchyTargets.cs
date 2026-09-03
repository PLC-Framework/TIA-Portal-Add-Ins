namespace Core.Adapters
{
    /// <summary>
    /// The four group roots a PLC exposes, as ports. The Add-In fills this in from the
    /// selected device; Core never learns which Siemens types are behind them.
    ///
    /// Any of them may be null when that family is not available.
    /// </summary>
    public sealed class HierarchyTargets
    {
        public IGroupNode Blocks { get; set; }
        public IGroupNode TechnologyObjects { get; set; }
        public IGroupNode TagTables { get; set; }
        public IGroupNode Types { get; set; }
    }
}
