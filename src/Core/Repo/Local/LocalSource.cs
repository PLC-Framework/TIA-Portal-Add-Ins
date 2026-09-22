using System;
using System.IO;

using Core.Config;

namespace Core.Repo.Local
{
    /// <summary>
    /// Where a core comes from, resolved to paths something can actually open.
    ///
    /// **The local half, beside <see cref="Remote.RemoteCopy"/> rather than through it.** Both
    /// end in the same two places in the project - the graph at <c>repo\core.json</c>, and the
    /// sources a download needs in <c>repo\tmp\</c> - so the catalogue, the comparison and the
    /// import never learn which half brought them. That used to be said of one mirrored folder;
    /// since 2026-09-22 nothing is mirrored, and it is true of those two places instead.
    ///
    /// **`folder` is the path inside the repository, and it is written with forward slashes**
    /// in a file that Windows will resolve. `plc/s7-1x00/core` is what the real configuration
    /// carries, so the separators are normalised here rather than at four call sites.
    /// </summary>
    public sealed class LocalSource
    {
        private LocalSource(string root, string folder, string coreFolder, string graphFile, string problem)
        {
            Root = root;
            Folder = folder;
            CoreFolder = coreFolder;
            GraphFile = graphFile;
            Problem = problem;
        }

        /// <summary>The repository root, which is what a node's <c>file</c> is relative to.</summary>
        public string Root { get; }

        /// <summary>
        /// The core's path inside the repository, as the configuration spells it and with
        /// forward slashes kept - because that is the form a node's <c>file</c> uses, and
        /// stripping one prefix from the other is what turns a node into a file on disk.
        /// </summary>
        public string Folder { get; }

        /// <summary>The core folder itself: <see cref="Root"/> and <see cref="Folder"/> joined.</summary>
        public string CoreFolder { get; }

        /// <summary>The dependency file inside the core folder, today <c>core.json</c>.</summary>
        public string GraphFile { get; }

        /// <summary>Why this could not be resolved, or null when it was.</summary>
        public string Problem { get; }

        public bool Resolved => Problem == null;

        /// <summary>
        /// The local repository named by <c>coreLocalRepositoryConfig</c>.
        ///
        /// **It does not check that any of it exists.** That is environmental - the same
        /// separation `EnvironmentValidator` already draws - and this runs inside TIA Portal,
        /// where a configuration prepared for another station is not wrong because a drive
        /// is not mapped here. <see cref="Exists"/> asks that question when somebody wants it
        /// asked.
        /// </summary>
        public static LocalSource Of(CoreLocalRepositoryConfig local)
        {
            if (local == null) return Failed("This project names no local repository.");

            if (string.IsNullOrWhiteSpace(local.Repository))
                return Failed("coreLocalRepositoryConfig.repository is empty.");

            if (string.IsNullOrWhiteSpace(local.Folder))
                return Failed("coreLocalRepositoryConfig.folder is empty.");

            if (string.IsNullOrWhiteSpace(local.DependencyFile))
                return Failed("coreLocalRepositoryConfig.dependencyFile is empty.");

            try
            {
                string root = Path.GetFullPath(local.Repository.Trim());
                string folder = local.Folder.Trim().Replace('\\', '/').Trim('/');
                string coreFolder = Path.GetFullPath(Path.Combine(root, folder.Replace('/', Path.DirectorySeparatorChar)));
                string graphFile = Path.Combine(coreFolder, local.DependencyFile.Trim());

                return new LocalSource(root, folder, coreFolder, graphFile, null);
            }
            catch (Exception exception)
            {
                // A path with a character Windows will not take, or one long enough to be
                // refused outright. Both are the configuration's problem, said in one line
                // rather than thrown into TIA's process.
                return Failed("The local repository path could not be read: " + exception.Message);
            }
        }

        /// <summary>
        /// Why this repository cannot be used on this machine right now, or null when it can.
        /// Environmental, so it is asked separately and its answer is allowed to be temporary.
        /// </summary>
        public string Exists()
        {
            if (!Resolved) return Problem;

            try
            {
                if (!Directory.Exists(Root)) return "The repository folder does not exist: " + Root;
                if (!Directory.Exists(CoreFolder)) return "The core folder does not exist: " + CoreFolder;
                if (!File.Exists(GraphFile)) return "The dependency file does not exist: " + GraphFile;

                return null;
            }
            catch (Exception exception)
            {
                return "The repository could not be read: " + exception.Message;
            }
        }

        private static LocalSource Failed(string problem) => new LocalSource(null, null, null, null, problem);
    }
}
