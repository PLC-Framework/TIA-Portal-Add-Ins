using System;
using System.Collections.Generic;
using System.IO;

using Core.Config;
using Core.Config.Validation;
using Core.DependencyGraph;
using Core.Repo.Local;
using Core.Repo.Remote;
using Core.Secrets;

namespace Core.Repo.PlcCore
{
    /// <summary>
    /// Brings what a project needs out of its core, in the two moments it needs it: the graph
    /// when the project is loaded, and a handful of sources when something is about to import
    /// them.
    ///
    /// **A Load copies the graph and nothing else** (2026-09-22, the maintainer's decision).
    /// It used to mirror the whole core folder into <c>repo\core\</c> and read the graph out of
    /// the mirror - 275 files for a comparison that reads one, since it is metadata only. The
    /// graph now lands at <c>repo\core.json</c> and every comparison runs against that copy,
    /// which keeps what the mirror was really for: the repository moves on, and a result has to
    /// be able to point at the core it was against.
    ///
    /// **A download brings its own sources**, through <see cref="Sources"/>, into
    /// <c>repo\tmp\</c> - exactly the ones it will write, from the branch's head, and flat.
    ///
    /// **Both go through one reading of the configuration**, so a Load and the download that
    /// follows it cannot disagree about which repository the core is in, which provider speaks
    /// for it or which token it takes.
    ///
    /// **A project that names no core is a normal state**, not a failure. Only the actions that
    /// read one need one, which is why `coreSource` may be null at all.
    /// </summary>
    public static class PlcCoreRefresh
    {
        /// <summary>
        /// Copies the core's graph into the project and reads it back.
        /// <paramref name="projectDirectory"/> is the TIA project's own folder, the one holding
        /// <c>.plc-framework\</c>.
        /// </summary>
        /// <param name="remote">
        /// How to reach a repository this machine does not have, or null. **`Core` cannot hold
        /// that itself** - it would pull `System.Net.Http` into TIA Portal's process - so a
        /// caller with a window supplies it and a caller without one simply cannot use a
        /// `remote` core, which is what the sentence says.
        /// </param>
        /// <param name="progress">Told what is happening while it waits on a wire.</param>
        public static PlcCoreRefreshResult Run(
            string projectDirectory, IRemoteCore remote = null, Action<string> progress = null)
        {
            Origin origin = Resolve(projectDirectory, remote);

            if (origin.Problem != null)
                return origin.NamesCore
                    ? PlcCoreRefreshResult.Failed(origin.Problem)
                    : PlcCoreRefreshResult.NoCore(origin.Problem);

            Retire(projectDirectory);

            return origin.Local != null
                ? FromLocal(projectDirectory, origin.Local)
                : FromRemote(projectDirectory, origin, progress);
        }

        /// <summary>
        /// Brings the sources of <paramref name="nodes"/> into the project's <c>repo\tmp\</c>,
        /// under the names <see cref="PlcCoreCatalog.FileOf"/> gives them - which is where the
        /// import that follows reads them.
        ///
        /// **From the branch's head, not from the commit the Load read** (the maintainer's
        /// rule: the repository is the source of truth). A source the head no longer lists is a
        /// problem named in the result, never something fetched from an older commit; and the
        /// result carries the commit it read, so a caller can see that the core moved since the
        /// comparison it is acting on.
        ///
        /// **A result that is not <c>Ready</c> should stop the import**, and both halves say why
        /// in their problems: an import missing one source is one whose dependency may never
        /// have arrived, and the object that needed it fails to build as the import's fault.
        /// </summary>
        public static PlcCoreSourcesResult Sources(
            string projectDirectory, IEnumerable<Node> nodes, IRemoteCore remote = null, Action<string> progress = null)
        {
            Origin origin = Resolve(projectDirectory, remote);

            if (origin.Problem != null) return PlcCoreSourcesResult.Refused(origin.Problem);

            string tmp = RepoPaths.TmpFor(projectDirectory);
            List<string> paths = new List<string>();

            foreach (Node node in nodes ?? new Node[0])
                if (!string.IsNullOrWhiteSpace(node?.File)) paths.Add(node.File);

            if (paths.Count == 0) return PlcCoreSourcesResult.Done(tmp, null, 0, 0, null);

            if (origin.Local != null)
            {
                LocalCopyResult copied = LocalCopy.Sources(origin.Local, paths, tmp);

                return copied.IsRefused
                    ? PlcCoreSourcesResult.Refused(copied.Refusal)
                    : PlcCoreSourcesResult.Done(tmp, null, copied.Files, 0, copied.Problems);
            }

            try
            {
                using (IRemoteFiles files = origin.Port.Open(origin.Remote, origin.Token))
                {
                    RemoteCopyResult fetched = RemoteCopy.Sources(files, origin.Remote.Folder, paths, tmp, progress);

                    return fetched.IsRefused
                        ? PlcCoreSourcesResult.Refused(fetched.Refusal)
                        : PlcCoreSourcesResult.Done(tmp, fetched.Commit, fetched.Downloaded, fetched.Kept, fetched.Problems);
                }
            }
            catch (Exception exception)
            {
                return PlcCoreSourcesResult.Refused(exception.Message);
            }
        }

        private static PlcCoreRefreshResult FromLocal(string projectDirectory, LocalSource repository)
        {
            LocalCopyResult copied = LocalCopy.Graph(repository, RepoPaths.GraphFor(projectDirectory));

            if (copied.IsRefused) return PlcCoreRefreshResult.Failed(copied.Refusal);

            // Read out of the copy, never out of the repository - which is the whole point of
            // having made one.
            PlcCoreLoadResult read = PlcCoreCatalogLoader.LoadFromProject(projectDirectory, repository.Folder);

            if (read.Catalog == null) return PlcCoreRefreshResult.Failed(read.Error);

            return PlcCoreRefreshResult.Read(
                read.Catalog, PlcCoreValidator.Validate(read.Catalog), copied, repository.CoreFolder);
        }

        /// <summary>
        /// The same thing from a repository nobody here hosts.
        ///
        /// **Nothing in here names a host**, and that is deliberate. Which API is spoken is
        /// <c>coreRemoteRepositoryConfig.provider</c>, answered by whoever supplies the port;
        /// this layer only checks that the framework knows the name and says it back in every
        /// sentence, so a GitLab project is never told about GitHub.
        ///
        /// **A graph that did not come down is refused**, and the previous copy is not compared
        /// against in its place: a Load that says "compared" is saying the core is the one the
        /// repository holds now.
        /// </summary>
        private static PlcCoreRefreshResult FromRemote(string projectDirectory, Origin origin, Action<string> progress)
        {
            CoreRemoteRepositoryConfig repository = origin.Remote;

            try
            {
                using (IRemoteFiles files = origin.Port.Open(repository, origin.Token))
                {
                    RemoteCopyResult copied = RemoteCopy.Graph(
                        files, repository.Folder, repository.DependencyFile, RepoPaths.GraphFor(projectDirectory), progress);

                    if (copied.IsRefused) return PlcCoreRefreshResult.Failed(copied.Refusal);

                    if (!copied.Ready)
                        return PlcCoreRefreshResult.Failed(
                            "The core's graph did not come down, so it is not compared against. " +
                            string.Join(" ", copied.Problems));

                    PlcCoreLoadResult read = PlcCoreCatalogLoader.LoadFromProject(projectDirectory, repository.Folder);

                    if (read.Catalog == null) return PlcCoreRefreshResult.Failed(read.Error);

                    // Built once and used twice: the marker on disk, for whoever looks into
                    // `repo\` later, and the line the window puts under the core tree now. Two
                    // renderings of one fact would be two things to keep in step.
                    CoreOrigin marker = CoreOrigin.Of(repository, copied);

                    CoreOrigin.Write(marker, projectDirectory);

                    return PlcCoreRefreshResult.Fetch(
                        read.Catalog, PlcCoreValidator.Validate(read.Catalog), copied, marker.ToString());
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
        /// Where the core comes from, read once for a Load and once for the download after it,
        /// and read the same way both times.
        /// </summary>
        private sealed class Origin
        {
            public string Problem;

            /// <summary>False only for "this project names no core", which is not a failure.</summary>
            public bool NamesCore = true;

            public LocalSource Local;

            public CoreRemoteRepositoryConfig Remote;
            public IRemoteCore Port;
            public string Token;

            public static Origin Refused(string problem, bool namesCore = true) =>
                new Origin { Problem = problem, NamesCore = namesCore };
        }

        private static Origin Resolve(string projectDirectory, IRemoteCore remote)
        {
            if (string.IsNullOrWhiteSpace(projectDirectory))
                return Origin.Refused("This project has no folder yet, so there is nowhere to copy a core into.");

            ConfigLoadResult loaded = ConfigLoader.LoadFromProject(projectDirectory);

            if (loaded.Config == null) return Origin.Refused(loaded.Error);

            string source = loaded.Config.Metadata?.CoreSource;

            // Absent and null are the same answer, and it is an answer rather than a fault.
            if (string.IsNullOrWhiteSpace(source))
                return Origin.Refused(
                    "This project names no core: metadata.coreSource is null. " +
                    "Set it in the Config. Editor to compare against one.", false);

            if (string.Equals(source, MetadataValidator.Remote, StringComparison.Ordinal))
                return Remote(loaded.Config.CoreRemoteRepositoryConfig, remote);

            if (!string.Equals(source, MetadataValidator.Local, StringComparison.Ordinal))
                return Origin.Refused("metadata.coreSource is '" + source + "', which is neither 'local' nor 'remote'.");

            LocalSource repository = LocalSource.Of(loaded.Config.CoreLocalRepositoryConfig);

            if (!repository.Resolved) return Origin.Refused(repository.Problem);

            // Environmental, and asked here rather than in LocalSource for the reason that type
            // records: a configuration prepared for another station is not wrong because a drive
            // is not mapped on this one. It is still a sentence, because nothing can be read
            // until it is.
            string missing = repository.Exists();

            return missing != null ? Origin.Refused(missing) : new Origin { Local = repository };
        }

        private static Origin Remote(CoreRemoteRepositoryConfig repository, IRemoteCore remote)
        {
            if (repository == null)
                return Origin.Refused(
                    "This project reads its core from a repository, but names none: " +
                    "coreRemoteRepositoryConfig is missing.");

            string provider = RepositoryValidator.ProviderOf(repository);

            // Asked here rather than left to the port: a client for one host handed another
            // host's configuration would read owner and repository off it and talk to the wrong
            // place, which is the one failure that would look like an empty core.
            if (!RepositoryValidator.Knows(provider))
                return Origin.Refused(
                    "This project reads its core from '" + provider + "', which this framework " +
                    "cannot speak. Today it knows " + RepositoryValidator.GitHub + ".");

            if (remote == null)
                return Origin.Refused(
                    "This project reads its core from " + provider + ", and this program cannot " +
                    "reach it. Open the core updater, which can.");

            return new Origin { Remote = repository, Port = remote, Token = Token(repository.Token) };
        }

        /// <summary>
        /// Takes away the <c>repo\core\</c> a project loaded before 2026-09-22 still carries: the
        /// whole core, mirrored, that nothing reads any more. **Best effort and silent** - it is
        /// generated, it is not versioned, and one that will not go today is tried again on the
        /// next Load; a failure here is never a reason to refuse a comparison.
        /// </summary>
        private static void Retire(string projectDirectory)
        {
            string retired = RepoPaths.RetiredCoreFor(projectDirectory);

            try
            {
                if (retired == null || !Directory.Exists(retired)) return;

                // A file copied off a read-only checkout arrived read-only, and Directory.Delete
                // refuses a whole tree over one of those.
                foreach (string file in Directory.GetFiles(retired, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, FileAttributes.Normal);

                Directory.Delete(retired, true);
            }
            catch (Exception)
            {
                // See above.
            }
        }

        /// <summary>
        /// The token behind whatever reference the configuration holds - <c>${REPO_TOKEN}</c>
        /// today, a provider's own the day there is a second one - or null.
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

        /// <summary>What copying the local graph did, or null when the core is remote.</summary>
        public LocalCopyResult Copied { get; }

        /// <summary>
        /// What came down the wire, or null when the core is local. **At most one of the two is
        /// ever set**: a core comes from one place.
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

    /// <summary>What came of bringing a download's sources into <c>repo\tmp\</c>.</summary>
    public sealed class PlcCoreSourcesResult
    {
        private static readonly string[] Nothing = new string[0];

        private PlcCoreSourcesResult(
            string folder, string commit, int brought, int kept, IReadOnlyList<string> problems, string refusal)
        {
            Folder = folder;
            Commit = commit;
            Brought = brought;
            Kept = kept;
            Problems = problems ?? Nothing;
            Refusal = refusal;
        }

        /// <summary>Where the sources are: the project's <c>repo\tmp\</c>.</summary>
        public string Folder { get; }

        /// <summary>
        /// The commit they were read at - the branch's head at that moment - or null for a local
        /// core, which has no commit to name. Held against the one the Load read, it says
        /// whether the core moved under the comparison the download was planned from.
        /// </summary>
        public string Commit { get; }

        /// <summary>How many were copied or came down.</summary>
        public int Brought { get; }

        /// <summary>How many were already there as the repository has them.</summary>
        public int Kept { get; }

        /// <summary>Sources that could not be brought, one line each.</summary>
        public IReadOnlyList<string> Problems { get; }

        /// <summary>Why nothing was attempted at all, or null.</summary>
        public string Refusal { get; }

        /// <summary>Every source asked for is there. Anything short of it should stop the import.</summary>
        public bool Ready => Refusal == null && Problems.Count == 0;

        internal static PlcCoreSourcesResult Done(
            string folder, string commit, int brought, int kept, IReadOnlyList<string> problems) =>
            new PlcCoreSourcesResult(folder, commit, brought, kept, problems, null);

        internal static PlcCoreSourcesResult Refused(string refusal) =>
            new PlcCoreSourcesResult(null, null, 0, 0, Nothing, refusal ?? "The sources could not be brought down.");
    }
}
