using Core.Config.Validation;
using Core.Repo.Remote;

namespace Core.Repo.PlcCore
{
    /// <summary>What came of refreshing a project's core: the catalogue, or why there is none.</summary>
    public sealed class PlcCoreRefreshResult
    {
        private PlcCoreRefreshResult(
            PlcCoreCatalog catalog, ValidationResult issues, RemoteCopyResult fetched,
            string source, string problem, bool names)
        {
            Catalog = catalog;
            Issues = issues;
            Fetched = fetched;
            Source = source;
            Problem = problem;
            NamesCore = names;
        }

        /// <summary>
        /// Where this core came from, in one line: the repository's core folder for a local one,
        /// and <c>owner/repo@branch on host, commit abc1234</c> for a remote one.
        ///
        /// **It is the live answer, not the marker read back.** `repo\core.origin.json` records
        /// the same thing for whoever opens the folder later; this is what the run that just
        /// happened knows, and a result must never describe a core other than the one it read.
        /// </summary>
        public string Source { get; }

        /// <summary>The core as the project now holds it, or null.</summary>
        public PlcCoreCatalog Catalog { get; }

        /// <summary>
        /// What is wrong with the graph that loaded, as `ValidationIssue`s - the config
        /// validators' own type, so one window shows both kinds.
        ///
        /// **Reported without stopping the comparison.** A core with a dangling edge still says
        /// which versions are current, and refusing to compare over a problem in the repository
        /// would punish the project for it.
        /// </summary>
        public ValidationResult Issues { get; }

        /// <summary>
        /// What came down the wire, or null when the core is local.
        ///
        /// **A local copy has no counterpart here, and had one until 2026-09-22 that nothing
        /// read.** It is one file: when it fails the refresh fails with its reason, and when it
        /// works there is nothing to say that <see cref="Source"/> does not already.
        /// </summary>
        public RemoteCopyResult Fetched { get; }

        /// <summary>Why there is no catalogue, or null when there is one.</summary>
        public string Problem { get; }

        /// <summary>
        /// Whether the project asks for a core at all.
        ///
        /// **False is not a failure**, and the difference matters on screen: "this project uses
        /// no core" is a fact about the project, where "the repository is not on this machine"
        /// is something to go and fix.
        /// </summary>
        public bool NamesCore { get; }

        public bool Ready => Catalog != null;

        public static PlcCoreRefreshResult Read(
            PlcCoreCatalog catalog, ValidationResult issues, string source) =>
            new PlcCoreRefreshResult(catalog, issues, null, source, null, true);

        public static PlcCoreRefreshResult Fetch(
            PlcCoreCatalog catalog, ValidationResult issues, RemoteCopyResult fetched, string source) =>
            new PlcCoreRefreshResult(catalog, issues, fetched, source, null, true);

        public static PlcCoreRefreshResult Failed(string problem) =>
            new PlcCoreRefreshResult(null, null, null, null, problem ?? "The core could not be read.", true);

        public static PlcCoreRefreshResult NoCore(string why) =>
            new PlcCoreRefreshResult(null, null, null, null, why, false);
    }
}
