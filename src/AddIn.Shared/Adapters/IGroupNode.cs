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
        ///
        /// **Null when it could not be created, and <paramref name="problem"/> says why.** It
        /// used to be null and nothing else, with the implementation expected to have dealt
        /// with the reason - which in practice meant swallowing it, so the operator was told
        /// the hierarchy was ready with a folder missing and nobody could say which or why.
        /// </summary>
        /// <param name="problem">Why it could not be created; null whenever a group comes back.</param>
        IGroupNode FindOrCreate(string name, out string problem);
    }
}
