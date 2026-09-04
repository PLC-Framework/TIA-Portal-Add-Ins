namespace AddIn.Shared.Adapters
{
    /// <summary>
    /// The group roots of one software unit, as ports. A unit exposes blocks, tag tables
    /// and types — there is no technology-object family inside a unit.
    ///
    /// Any of them may be null when that family is not available.
    /// </summary>
    public sealed class HierarchySoftwareUnitTargets
    {
        /// <summary>Only used to name the unit in messages.</summary>
        public string Name { get; set; }

        public IGroupNode Blocks { get; set; }
        public IGroupNode TagTables { get; set; }
        public IGroupNode Types { get; set; }
    }
}
