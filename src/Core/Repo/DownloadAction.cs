namespace Core.Repo
{
    /// <summary>What a download would do with one node.</summary>
    public enum DownloadAction
    {
        /// <summary>The project has nothing of this name.</summary>
        Import,

        /// <summary>
        /// The project has it, and this would write over it - **and move it into the folder its
        /// family names** when it is anywhere else, which TIA would not do on its own.
        /// </summary>
        Replace,

        /// <summary>
        /// Left as it is: a dependency already at the version the core stands behind, by
        /// default, or anything the operator unticked when asked.
        /// </summary>
        Skip
    }
}
