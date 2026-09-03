namespace Core.Adapters
{
    /// <summary>
    /// A node in a tree of groups that can be created on demand.
    ///
    /// TIA exposes four separate group families (blocks, technology objects, tag tables
    /// and types) that share no base type and no interface, even though all four offer
    /// the same Find/Create pair. This port is what lets the walk be written once.
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
