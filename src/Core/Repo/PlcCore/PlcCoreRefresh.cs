using System;
using System.Collections.Generic;
using System.IO;

using Core.Config;
using Core.Config.Validation;
using Core.Repo.Local;
using Core.Repo.PlcCore.Graph;
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
            string projectDirectory, IRemotePlcCore remote = null, Action<string> progress = null)
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
            string projectDirectory, IEnumerable<Node> nodes, IRemotePlcCore remote = null, Action<string> progress = null)
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
                read.Catalog, PlcCoreValidator.Validate(read.Catalog), repository.CoreFolder);
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
                    PlcCoreOrigin marker = PlcCoreOrigin.Of(repository, copied);

                    PlcCoreOrigin.Write(marker, projectDirectory);

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
            public IRemotePlcCore Port;
            public string Token;

            public static Origin Refused(string problem, bool namesCore = true) =>
                new Origin { Problem = problem, NamesCore = namesCore };
        }

        private static Origin Resolve(string projectDirectory, IRemotePlcCore remote)
        {
            if (string.IsNullOrWhiteSpace(projectDirectory))
                return Origin.Refused("This project has no folder yet, so there is nowhere to copy a core into.");

            ConfigLoadResult loaded = ConfigLoader.LoadFromProject(projectDirectory);

            if (loaded.Config == null) return Origin.Refused(loaded.Error);

            // Absent and null are the same answer, and it is an answer rather than a fault. Only
            // null, though: an empty string is a value outside the set, which is how the contract
            // reads it and what the validator below says - this read it as "no core" until
            // 2026-09-22, turning a half-typed word into a decision nobody made.
            if (loaded.Config.Metadata != null && loaded.Config.Metadata.CoreSource == null)
                return Origin.Refused(
                    "This project names no core: metadata.coreSource is null. " +
                    "Set it in the Config. Editor to compare against one.", false);

            // **This concern's own validator, before anything touches the disk or the wire** -
            // the rule the hierarchy and the coding-style check already keep: hand-editing
            // bypasses the editor, so what the editor would refuse is refused here too, each
            // problem named by its path in the file.
            ValidationResult check = RepositoryValidator.Validate(loaded.Config);

            if (!check.IsValid) return Origin.Refused(Invalid(check));

            if (MetadataValidator.SourceOf(loaded.Config.Metadata) == MetadataValidator.Remote)
                return Remote(loaded.Config.CoreRemoteRepositoryConfig, remote);

            LocalSource repository = LocalSource.Of(loaded.Config.CoreLocalRepositoryConfig);

            // The fields are all there by now; what is left is a path Windows will not take,
            // which only resolving it can find.
            if (!repository.Resolved) return Origin.Refused(repository.Problem);

            // Environmental, and asked here rather than in LocalSource for the reason that type
            // records: a configuration prepared for another station is not wrong because a drive
            // is not mapped on this one. It is still a sentence, because nothing can be read
            // until it is.
            string missing = repository.Exists();

            return missing != null ? Origin.Refused(missing) : new Origin { Local = repository };
        }

        /// <summary>
        /// A remote section the validator has already passed: present, every required field
        /// filled, an absolute `apiUrl`, and a provider this framework speaks.
        ///
        /// **The provider is still refused before a client ever sees it**, which is the refusal
        /// that matters - a client for one host handed another host's configuration would read
        /// owner and repository off it and talk to the wrong place, the one failure that would
        /// look like an empty core. It is simply the validator's refusal now rather than a second
        /// copy of it written out here.
        /// </summary>
        private static Origin Remote(CoreRemoteRepositoryConfig repository, IRemotePlcCore remote)
        {
            string provider = RepositoryValidator.ProviderOf(repository);

            if (remote == null)
                return Origin.Refused(
                    "This project reads its core from " + provider + ", and this program cannot " +
                    "reach it. Open the core updater, which can.");

            return new Origin { Remote = repository, Port = remote, Token = Token(repository.Token) };
        }

        /// <summary>How many problems a refusal names before it counts the rest - the hierarchy's number.</summary>
        private const int ListedProblems = 10;

        /// <summary>
        /// The validator's problems as one sentence, each by its path in the file. **One line**,
        /// unlike the hierarchy's refusal: that one is a message box, and this lands under the
        /// core tree in a caption that trims with the whole of it on hover.
        /// </summary>
        private static string Invalid(ValidationResult check)
        {
            List<string> named = new List<string>();

            foreach (ValidationIssue issue in check.Issues)
            {
                if (named.Count == ListedProblems) break;
                named.Add(issue.ToString());
            }

            int rest = check.Issues.Count - named.Count;

            // Each message is a sentence already - "Required.", "Must be github, ..." - so they
            // are strung together as sentences rather than joined with a separator.
            return "The core cannot be read: " + ConfigPaths.File + " has problems - " + string.Join(" ", named) +
                   (rest > 0 ? " And " + rest + " more." : string.Empty) + " Open the Config. Editor to fix them.";
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
}
