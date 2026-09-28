namespace Core.Imports
{
    /// <summary>What became of one file.</summary>
    public enum ImportOutcome
    {
        /// <summary>It went in, and met nothing that was already there.</summary>
        Imported,

        /// <summary>It went in over what was already there - where that was, not where it was sent.</summary>
        Overwritten,

        /// <summary>The operator said no to overwriting, so nothing was asked of TIA.</summary>
        Left,

        /// <summary>TIA would not take it, and said why.</summary>
        Refused
    }
}
