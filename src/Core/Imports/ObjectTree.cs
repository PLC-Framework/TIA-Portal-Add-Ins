namespace Core.Imports
{
    /// <summary>
    /// Which of a PLC's trees an import goes into - the one whose folder was right-clicked.
    /// **One per launch** (the maintainer's decision, 2026-09-28): the menu entry is on object
    /// folders only, so the destination always belongs to exactly one of these.
    /// </summary>
    public enum ObjectTree
    {
        /// <summary>Program blocks: OBs, FCs, FBs and data blocks.</summary>
        Blocks,

        /// <summary>PLC data types.</summary>
        Types,

        /// <summary>PLC tags: tag tables.</summary>
        TagTables,

        /// <summary>Technology objects.</summary>
        TechnologyObjects
    }
}
