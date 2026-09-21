using System;

using Core.Config;
using Core.Config.Validation;
using Core.Repo.GitHub;
using Core.Repo.Local;
using Core.Repo.Remote;
using Core.Secrets;

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
        /// <param name="remote">
        /// How to reach a repository this machine does not have, or null. **`Core` cannot hold
        /// that itself** - it would pull `System.Net.Http` into TIA Portal's process - so a
        /// caller with a window supplies it and a caller without one simply cannot use a
        /// `remote` core, which is what the sentence says.
        /// </param>
        /// <param name="progress">
        /// Told what is happening while a couple of hundred files come down a wire. A local
        /// core never needs it; a remote one on a slow line very much does.
        /// </param>
        public static CoreRefreshResult Run(
            string projectDirectory, IRemoteCore remote = null, Action<string> progress = null)
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

            if (string.Equals(source, MetadataValidator.Remote, StringComparison.Ordinal))
                return FromGitHub(projectDirectory, loaded.Config.CoreRemoteRepositoryConfig, remote, progress);

            if (!string.Equals(source, MetadataValidator.Local, StringComparison.Ordinal))
                return CoreRefreshResult.Failed(
                    "metadata.coreSource is '" + source + "', which is neither 'local' nor 'remote'.");

            LocalSource repository = LocalSource.Of(loaded.Config.CoreLocalRepositoryConfig);

            if (!repository.Resolved) return CoreRefreshResult.Failed(repository.Problem);

            // Environmental, and asked here rather than in LocalSource for the reason that type
            // records: a configuration prepared for another station is not wrong because a drive
            // is not mapped on this one. It is still a sentence, because nothing can be compared
            // until it is.
            string missing = repository.Exists();

            if (missing != null) return CoreRefreshResult.Failed(missing);

            LocalCopyResult copied = LocalCopy.Mirror(repository.CoreFolder, RepoPaths.CoreFor(projectDirectory));

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

        /// <summary>
        /// The same thing from a repository on GitHub: bring the core down into
        /// <c>repo\core\</c>, then read it out of the copy exactly as the local one is.
        ///
        /// **Everything past the download is shared with the local core**, which is the whole
        /// point of a copy: the catalogue, the validator, the comparison and a download's own
        /// sources all read a folder, and none of them learns where it came from.
        ///
        /// **A core that did not come down whole is refused.** The copy is read as the truth
        /// about what the core defines, so a file short would be read as the core not defining
        /// something - "you are missing a block" told about a download that failed.
        /// </summary>
        private static CoreRefreshResult FromGitHub(
            string projectDirectory, CoreRemoteRepositoryConfig repository, IRemoteCore remote, Action<string> progress)
        {
            if (repository == null)
                return CoreRefreshResult.Failed(
                    "This project reads its core from GitHub, but names no repository: " +
                    "coreRemoteRepositoryConfig is missing.");

            if (remote == null)
                return CoreRefreshResult.Failed(
                    "This project reads its core from GitHub, and this program cannot reach it. " +
                    "Open the core updater, which can.");

            string token = Token(repository.Token);

            try
            {
                using (IRemoteFiles files = remote.Open(repository, token))
                {
                    RemoteCopyResult copied = RemoteCopy.Mirror(
                        files,
                        repository.Folder,
                        RepoPaths.CoreFor(projectDirectory),
                        RepoPaths.TmpFor(projectDirectory),
                        progress);

                    if (copied.IsRefused) return CoreRefreshResult.Failed(copied.Refusal);

                    if (!copied.Ready)
                        return CoreRefreshResult.Failed(
                            "The core did not come down whole, so it is not compared against. " +
                            string.Join(" ", copied.Problems));

                    CoreLoadResult read = CoreCatalogLoader.LoadFromProject(
                        projectDirectory, repository.Folder, repository.DependencyFile);

                    if (read.Catalog == null) return CoreRefreshResult.Failed(read.Error);

                    CoreOrigin.Write(
                        CoreOrigin.Of(repository, copied, copied.Downloaded + copied.Kept), projectDirectory);

                    return CoreRefreshResult.Fetch(read.Catalog, CoreValidator.Validate(read.Catalog), copied);
                }
            }
            catch (Exception exception)
            {
                // Opening the repository, or disposing it. The client's own refusals are
                // already sentences; anything else is at least named rather than thrown into
                // a window that would report "Failed."
                return CoreRefreshResult.Failed(exception.Message);
            }
        }

        /// <summary>
        /// The token behind <c>${GITHUB_TOKEN}</c>, or null.
        ///
        /// **`config.json` never holds the secret** - it holds the reference, because that
        /// file lives inside a TIA project and TIA projects are under version control. The
        /// value is in the per-user `.env`, and a reference nothing answers stays unresolved
        /// rather than becoming an empty string: null here is "no token", which is exactly how
        /// a public repository is read, and the refusal that follows for a private one names
        /// the variable to set.
        /// </summary>
        private static string Token(string configured)
        {
            if (string.IsNullOrWhiteSpace(configured)) return null;

            string expanded = Variables.Expand(configured.Trim(), DotEnv.Get);

            // Still a reference: nobody answered it.
            return Variables.References(expanded).Count > 0 ? null : expanded;
        }
    }

    /// <summary>What came of refreshing a project's core: the catalogue, or why there is none.</summary>
    public sealed class CoreRefreshResult
    {
        private CoreRefreshResult(
            CoreCatalog catalog, ValidationResult issues, LocalCopyResult copied, RemoteCopyResult fetched,
            string problem, bool names)
        {
            Catalog = catalog;
            Issues = issues;
            Copied = copied;
            Fetched = fetched;
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

        /// <summary>What the local mirror copied and removed, or null when the core is remote.</summary>
        public LocalCopyResult Copied { get; }

        /// <summary>
        /// What came down the wire, or null when the core is local. **At most one of the two
        /// is ever set**: a core comes from one place, and which one is the difference between
        /// "17 files copied" and "17 downloaded, 232 already here".
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

        public static CoreRefreshResult Read(CoreCatalog catalog, ValidationResult issues, LocalCopyResult copied) =>
            new CoreRefreshResult(catalog, issues, copied, null, null, true);

        public static CoreRefreshResult Fetch(CoreCatalog catalog, ValidationResult issues, RemoteCopyResult fetched) =>
            new CoreRefreshResult(catalog, issues, null, fetched, null, true);

        public static CoreRefreshResult Failed(string problem) =>
            new CoreRefreshResult(null, null, null, null, problem ?? "The core could not be read.", true);

        public static CoreRefreshResult NoCore(string why) =>
            new CoreRefreshResult(null, null, null, null, why, false);
    }
}
