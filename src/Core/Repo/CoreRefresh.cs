using Core.Config;
using Core.Config.Validation;

namespace Core.Repo
{
    /// <summary>
    /// Brings a project's copy of the core up to date and reads it back: configuration, then
    /// the repository, then the mirror into <c>repo\core\</c>, then <c>core.json</c>.
    ///
    /// **Every piece of this was built and tested in stage 1 and called by nothing.** This is
    /// the line that joins them, and it is deliberately one call rather than five steps a
    /// window has to get in the right order - the copy has to happen before the catalogue is
    /// read out of the copy, and a caller that got that backwards would compare against
    /// whatever the last run left behind.
    ///
    /// **It always copies first.** Reading the repository in place would describe a core nobody
    /// can point at afterwards: the repository moves on and the result keeps claiming something
    /// it can no longer reproduce. With a copy, both halves of the comparison are on disk side
    /// by side for as long as the project keeps them.
    ///
    /// **A project that names no core is a normal state**, not a failure. Only the actions that
    /// read one need one, which is why `coreSource` may be null at all.
    /// </summary>
    public static class CoreRefresh
    {
        /// <summary>
        /// <paramref name="projectDirectory"/> is the TIA project's own folder, the one holding
        /// <c>.plc-framework\</c>.
        /// </summary>
        public static CoreRefreshResult Run(string projectDirectory)
        {
            if (string.IsNullOrWhiteSpace(projectDirectory))
                return CoreRefreshResult.Failed("This project has no folder yet, so there is nowhere to copy a core into.");

            ConfigLoadResult loaded = ConfigLoader.LoadFromProject(projectDirectory);

            if (loaded.Config == null) return CoreRefreshResult.Failed(loaded.Error);

            string source = loaded.Config.Metadata?.CoreSource;

            // Absent and null are the same answer, and it is an answer rather than a fault.
            if (string.IsNullOrWhiteSpace(source))
                return CoreRefreshResult.NoCore(
                    "This project names no core: metadata.coreSource is null. " +
                    "Set it in the Config. Editor to compare against one.");

            if (string.Equals(source, MetadataValidator.Remote, System.StringComparison.Ordinal))
                return CoreRefreshResult.NoCore(
                    "This project reads its core from GitHub, and that is not wired up yet. " +
                    "A local repository works today.");

            if (!string.Equals(source, MetadataValidator.Local, System.StringComparison.Ordinal))
                return CoreRefreshResult.Failed(
                    "metadata.coreSource is '" + source + "', which is neither 'local' nor 'remote'.");

            RepoSource repository = RepoSource.Local(loaded.Config.CoreLocalRepositoryConfig);

            if (!repository.Resolved) return CoreRefreshResult.Failed(repository.Problem);

            // Environmental, and asked here rather than in RepoSource for the reason that type
            // records: a configuration prepared for another station is not wrong because a drive
            // is not mapped on this one. It is still a sentence, because nothing can be compared
            // until it is.
            string missing = repository.Exists();

            if (missing != null) return CoreRefreshResult.Failed(missing);

            RepoCopyResult copied = RepoCopy.Mirror(repository.CoreFolder, RepoPaths.CoreFor(projectDirectory));

            if (copied.IsRefused) return CoreRefreshResult.Failed(copied.Refusal);

            // Read out of the copy, never out of the repository - which is the whole point of
            // having made one.
            CoreLoadResult read = CoreCatalogLoader.LoadFromProject(
                projectDirectory,
                repository.Folder,
                loaded.Config.CoreLocalRepositoryConfig?.DependencyFile);

            if (read.Catalog == null) return CoreRefreshResult.Failed(read.Error);

            return CoreRefreshResult.Read(read.Catalog, CoreValidator.Validate(read.Catalog), copied);
        }
    }

    /// <summary>What came of refreshing a project's core: the catalogue, or why there is none.</summary>
    public sealed class CoreRefreshResult
    {
        private CoreRefreshResult(
            CoreCatalog catalog, ValidationResult issues, RepoCopyResult copied, string problem, bool names)
        {
            Catalog = catalog;
            Issues = issues;
            Copied = copied;
            Problem = problem;
            NamesCore = names;
        }

        /// <summary>The core as the project now holds it, or null.</summary>
        public CoreCatalog Catalog { get; }

        /// <summary>
        /// What is wrong with the graph that loaded, as `ValidationIssue`s - the config
        /// validators' own type, so one window shows both kinds.
        ///
        /// **Reported without stopping the comparison.** A core with a dangling edge still says
        /// which versions are current, and refusing to compare over a problem in the repository
        /// would punish the project for it.
        /// </summary>
        public ValidationResult Issues { get; }

        /// <summary>What the mirror copied and removed, or null when none was made.</summary>
        public RepoCopyResult Copied { get; }

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

        public static CoreRefreshResult Read(CoreCatalog catalog, ValidationResult issues, RepoCopyResult copied) =>
            new CoreRefreshResult(catalog, issues, copied, null, true);

        public static CoreRefreshResult Failed(string problem) =>
            new CoreRefreshResult(null, null, null, problem ?? "The core could not be read.", true);

        public static CoreRefreshResult NoCore(string why) =>
            new CoreRefreshResult(null, null, null, why, false);
    }
}
