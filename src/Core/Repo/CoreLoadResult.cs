namespace Core.Repo
{
    /// <summary>
    /// Outcome of reading <c>core.json</c>. Shaped like <c>ConfigLoadResult</c> deliberately:
    /// the failure travels as a sentence rather than as an exception, because every consumer
    /// wants the message and none of them wants the stack.
    /// </summary>
    public sealed class CoreLoadResult
    {
        private CoreLoadResult(CoreCatalog catalog, string error)
        {
            Catalog = catalog;
            Error = error;
        }

        /// <summary>The core that was read, or null when reading failed.</summary>
        public CoreCatalog Catalog { get; }

        /// <summary>Why reading failed, or null when it succeeded.</summary>
        public string Error { get; }

        public bool Succeeded => Catalog != null;

        internal static CoreLoadResult Loaded(CoreCatalog catalog) => new CoreLoadResult(catalog, null);

        internal static CoreLoadResult Failed(string error) => new CoreLoadResult(null, error);
    }
}
