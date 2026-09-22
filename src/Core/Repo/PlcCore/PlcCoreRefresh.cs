using System;

using Core.Config;
using Core.Config.Validation;
using Core.Repo.Local;
using Core.Repo.Remote;
using Core.Secrets;

namespace Core.Repo.PlcCore
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
    public static class PlcCoreRefresh
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
        public static PlcCoreRefreshResult Run(
            string projectDirectory, IRemoteCore remote = null, Action<string> progress = null)
        {
            if (string.IsNullOrWhiteSpace(projectDirectory))
                return PlcCoreRefreshResult.Failed("This project has no folder yet, so there is nowhere to copy a core into.");

            ConfigLoadResult loaded = ConfigLoader.LoadFromProject(projectDirectory);

            if (loaded.Config == null) return PlcCoreRefreshResult.Failed(loaded.Error);

            string source = loaded.Config.Metadata?.CoreSource;

            // Absent and null are the same answer, and it is an answer rather than a fault.
            if (string.IsNullOrWhiteSpace(source))
                return PlcCoreRefreshResult.NoCore(
                    "This project names no core: metadata.coreSource is null. " +
                    "Set it in the Config. Editor to compare against one.");

            if (string.Equals(source, MetadataValidator.Remote, StringComparison.Ordinal))
                return FromRemote(projectDirectory, loaded.Config.CoreRemoteRepositoryConfig, remote, progress);

            if (!string.Equals(source, MetadataValidator.Local, StringComparison.Ordinal))
                return PlcCoreRefreshResult.Failed(
                    "metadata.coreSource is '" + source + "', which is neither 'local' nor 'remote'.");

            LocalSource repository = LocalSource.Of(loaded.Config.CoreLocalRepositoryConfig);

            if (!repository.Resolved) return PlcCoreRefreshResult.Failed(repository.Problem);

            // Environmental, and asked here rather than in LocalSource for the reason that type
            // records: a configuration prepared for another station is not wrong because a drive
            // is not mapped on this one. It is still a sentence, because nothing can be compared
            // until it is.
            string missing = repository.Exists();

            if (missing != null) return PlcCoreRefreshResult.Failed(missing);

            LocalCopyResult copied = LocalCopy.Mirror(repository.CoreFolder, RepoPaths.CoreFor(projectDirectory));

            if (copied.IsRefused) return PlcCoreRefreshResult.Failed(copied.Refusal);

            // Read out of the copy, never out of the repository - which is the whole point of
            // having made one.
            PlcCoreLoadResult read = PlcCoreCatalogLoader.LoadFromProject(
                projectDirectory,
                repository.Folder,
                loaded.Config.CoreLocalRepositoryConfig?.DependencyFile);

            if (read.Catalog == null) return PlcCoreRefreshResult.Failed(read.Error);

            return PlcCoreRefreshResult.Read(
                read.Catalog, PlcCoreValidator.Validate(read.Catalog), copied, repository.CoreFolder);
        }

        /// <summary>
        /// The same thing from a repository nobody here hosts: bring the core down into
        /// <c>repo\core\</c>, then read it out of the copy exactly as the local one is.
        ///
        /// **Nothing in here names a host**, and that is deliberate. Which API is spoken is
        /// <c>coreRemoteRepositoryConfig.provider</c>, answered by whoever supplies the port;
        /// this layer only checks that the framework knows the name and says it back in every
        /// sentence, so a GitLab project is never told about GitHub.
        ///
        /// **Everything past the download is shared with the local core**, which is the whole
        /// point of a copy: the catalogue, the validator, the comparison and a download's own
        /// sources all read a folder, and none of them learns where it came from.
        ///
        /// **A core that did not come down whole is refused.** The copy is read as the truth
        /// about what the core defines, so a file short would be read as the core not defining
        /// something - "you are missing a block" told about a download that failed.
        /// </summary>
        private static PlcCoreRefreshResult FromRemote(
            string projectDirectory, CoreRemoteRepositoryConfig repository, IRemoteCore remote, Action<string> progress)
        {
            if (repository == null)
                return PlcCoreRefreshResult.Failed(
                    "This project reads its core from a repository, but names none: " +
                    "coreRemoteRepositoryConfig is missing.");

            string provider = RepositoryValidator.ProviderOf(repository);

            // Asked here rather than left to the port: a client for one host handed another
            // host's configuration would read owner and repository off it and talk to the wrong
            // place, which is the one failure that would look like an empty core.
            if (!RepositoryValidator.Knows(provider))
                return PlcCoreRefreshResult.Failed(
                    "This project reads its core from '" + provider + "', which this framework " +
                    "cannot speak. Today it knows " + RepositoryValidator.GitHub + ".");

            if (remote == null)
                return PlcCoreRefreshResult.Failed(
                    "This project reads its core from " + provider + ", and this program cannot " +
                    "reach it. Open the core updater, which can.");

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

                    if (copied.IsRefused) return PlcCoreRefreshResult.Failed(copied.Refusal);

                    if (!copied.Ready)
                        return PlcCoreRefreshResult.Failed(
                            "The core did not come down whole, so it is not compared against. " +
                            string.Join(" ", copied.Problems));

                    PlcCoreLoadResult read = PlcCoreCatalogLoader.LoadFromProject(
                        projectDirectory, repository.Folder, repository.DependencyFile);

                    if (read.Catalog == null) return PlcCoreRefreshResult.Failed(read.Error);

                    // Built once and used twice: the marker on disk, for whoever looks into
                    // `repo\` later, and the line the window puts under the core tree now. Two
                    // renderings of one fact would be two things to keep in step.
                    CoreOrigin origin = CoreOrigin.Of(repository, copied, copied.Downloaded + copied.Kept);

                    CoreOrigin.Write(origin, projectDirectory);

                    return PlcCoreRefreshResult.Fetch(
                        read.Catalog, PlcCoreValidator.Validate(read.Catalog), copied, origin.ToString());
                }
            }
            catch (Exception exception)
            {
                // Opening the repository, or disposing it. The client's own refusals are
                // already sentences; anything else is at least named rather than thrown into
                // a window that would report "Failed."
                return PlcCoreRefreshResult.Failed(exception.Message);
            }
        }

        /// <summary>
        /// The token behind whatever reference the configuration holds - <c>${REPO_TOKEN}</c>
        /// today, <c>${GITLAB_TOKEN}</c> the day there is one - or null.
        ///
        /// **Nothing here knows the variable's name**, which is what lets a second provider
        /// arrive without touching this: the file names the variable and the `.env` answers it.
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
    public sealed class PlcCoreRefreshResult
    {
        private PlcCoreRefreshResult(
            PlcCoreCatalog catalog, ValidationResult issues, LocalCopyResult copied, RemoteCopyResult fetched,
            string source, string problem, bool names)
        {
            Catalog = catalog;
            Issues = issues;
            Copied = copied;
            Fetched = fetched;
            Source = source;
            Problem = problem;
            NamesCore = names;
        }

        /// <summary>
        /// Where this core came from, in one line: the repository folder for a local one, and
        /// <c>owner/repo@branch on host, commit abc1234</c> for a remote one.
        ///
        /// **It is the live answer, not the marker read back.** `repo\core.origin.json` records
        /// the same thing for whoever opens the folder later; this is what the run that just
        /// happened knows, and a result must never describe a core other than the one it read.
        ///
        /// **Both halves answer it, which is the point.** Until now nothing could say which core
        /// a comparison was against - and a local core is exactly the case that needed it most,
        /// being named by a folder that moves on with nothing recording which state it was in.
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

        public static PlcCoreRefreshResult Read(
            PlcCoreCatalog catalog, ValidationResult issues, LocalCopyResult copied, string source) =>
            new PlcCoreRefreshResult(catalog, issues, copied, null, source, null, true);

        public static PlcCoreRefreshResult Fetch(
            PlcCoreCatalog catalog, ValidationResult issues, RemoteCopyResult fetched, string source) =>
            new PlcCoreRefreshResult(catalog, issues, null, fetched, source, null, true);

        public static PlcCoreRefreshResult Failed(string problem) =>
            new PlcCoreRefreshResult(null, null, null, null, null, problem ?? "The core could not be read.", true);

        public static PlcCoreRefreshResult NoCore(string why) =>
            new PlcCoreRefreshResult(null, null, null, null, null, why, false);
    }
}
