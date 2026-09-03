namespace AddIn.Shared.Adapters
{
    /// <summary>
    /// A node in a tree of groups that can be created on demand.
    ///
    /// TIA exposes four separate group families (blocks, technology objects, tag tables
    /// and types) that share no base type and no interface, even though all four offer
    /// the same Find/Create pair. This port is what lets the walk be written once.
    ///
    /// It lives here rather than in Core because it speaks of PLC group structure: that
    /// vocabulary belongs to the Add-In's domain, not to the shared one.
    /// </summary>
    public interface IGroupNode
    {
        /// <summary>
        /// The existing child group with that name, or a newly created one.
        /// Null when it could not be created; the implementation is expected to have
        /// dealt with the reason, so callers just skip it.
        /// </summary>
        IGroupNode FindOrCreate(string name);
    }
}
