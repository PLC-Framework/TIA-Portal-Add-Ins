namespace Core.Repo
{
    /// <summary>What the project has of one version the core still stands behind.</summary>
    public enum NodeState
    {
        /// <summary>The project holds this exact version.</summary>
        Held,

        /// <summary>
        /// The project holds this name at some other version - which may be a retired one, or one
        /// the core does not define at all. **Not called "older"**: nothing here compares one
        /// version as greater than another, and a project can perfectly well be ahead.
        /// </summary>
        AtAnotherVersion,

        /// <summary>The project has nothing of this name.</summary>
        Absent,

        /// <summary>
        /// The map was filtered and did not cover this, so whether the project has it is not
        /// something this map can answer. **A state of its own rather than "absent"**, because
        /// reporting 91 data types as missing from a project holding every one is the silent hole
        /// the filter was recorded to prevent.
        /// </summary>
        Unknown
    }
}
