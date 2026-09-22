using System.IO;

using Core.Config;

namespace Core.Repo
{
    /// <summary>
    /// What lives inside <c>.plc-framework\repo\</c>, the project's own workspace for the
    /// core it is built on.
    ///
    /// <code>
    /// &lt;TIA project&gt;\.plc-framework\repo\
    /// +-- core.json         the core's graph, pinned: what every comparison is against
    /// +-- core.origin.json  where it came from and at which commit, for a remote core
    /// +-- project.json      what the TIA project actually holds, as this read it
    /// +-- tmp\              the sources a download is about to import
    /// +-- stranded\         an object's export while it is out of the project - and after,
    ///                       when it could not go back in
    /// </code>
    ///
    /// **There is no copy of the core's sources here, and that was a correction** (2026-09-22,
    /// the maintainer's decision). Until then a Load mirrored the whole core folder into
    /// <c>repo\core\</c> - 275 files, in every project - although the comparison reads exactly
    /// one of them: it is metadata only, and the metadata is <c>core.json</c>. A project uses a
    /// handful of the core's libraries, so the rest was copied for nothing, and remotely it
    /// cost a request per file on an hourly budget of sixty without a token.
    ///
    /// **What the old rule protected still holds.** A comparison is made against a copy in
    /// the project rather than against the repository in place, because a repository moves on
    /// and a result must be able to point at what it was against - and what it was against is
    /// the graph, which is still copied, and still pinned by its commit. The sources are
    /// brought into <c>tmp\</c> only when an import is about to read them, and only those.
    ///
    /// **None of it is versioned**, and nothing has to be done for that: the `.gitignore`
    /// the config editor writes is an allow-list of three files, so everything here is
    /// ignored the day it appears. That was the point of an allow-list.
    ///
    /// Pure, like <see cref="ConfigPaths"/>: it builds strings and creates nothing.
    /// </summary>
    public static class RepoPaths
    {
        /// <summary>
        /// The core's graph, copied out of the repository whatever the repository calls it -
        /// <c>dependencyFile</c> names the file there; here it is always this one name, so
        /// nothing downstream has to read the configuration to find it.
        /// </summary>
        public const string GraphFile = "core.json";

        /// <summary>
        /// What the TIA project holds, written by the walk rather than by the repository -
        /// the other half of every comparison.
        /// </summary>
        public const string ProjectFile = "project.json";

        /// <summary>
        /// Where a source lands before TIA reads it, and where V17-V20 reads a title out of an
        /// export. **Cleared whenever a run ends**, because nothing in it is ever the only copy
        /// of anything - that is what <see cref="Stranded"/> is for.
        /// </summary>
        public const string Tmp = "tmp";

        /// <summary>
        /// Where an object's export waits while the object is out of the project - a move, or a
        /// download taking it out of the wrong folder - and where it stays when it could not go
        /// back in. See <see cref="StrandedFiles"/>.
        ///
        /// **Its own folder since 2026-09-22, and that was a fix.** These exports lived in
        /// <see cref="Tmp"/>, and a run that stranded one kept the folder rather than clearing
        /// it; but the next run that ended well cleared it, and so did every V17-V20 Load, whose
        /// map reads titles through the same folder. The only copy of a block went with it.
        /// Nothing ever clears this one.
        /// </summary>
        public const string Stranded = "stranded";

        /// <summary>
        /// The folder the sources used to be mirrored into. Named only so that a Load can take
        /// it away from a project that still has one - it holds nothing anything reads.
        /// </summary>
        internal const string RetiredCoreFolder = "core";

        /// <summary>
        /// The workspace folder of one TIA project, or null when the project directory is
        /// unknown - which is the case for a window started by hand.
        /// </summary>
        public static string For(string projectDirectory) =>
            ConfigPaths.FolderFor(projectDirectory, ConfigPaths.Repo);

        /// <summary>Where the core's graph goes, or null when the project directory is unknown.</summary>
        public static string GraphFor(string projectDirectory) => Under(projectDirectory, GraphFile);

        /// <summary>Where the project map goes, or null when the project directory is unknown.</summary>
        public static string ProjectFor(string projectDirectory) => Under(projectDirectory, ProjectFile);

        /// <summary>Where a download works, or null when the project directory is unknown.</summary>
        public static string TmpFor(string projectDirectory) => Under(projectDirectory, Tmp);

        /// <summary>Where an object waits out of the project, or null when the project directory is unknown.</summary>
        public static string StrandedFor(string projectDirectory) => Under(projectDirectory, Stranded);

        /// <summary>
        /// Where one of the core's sources lands before an import reads it: <c>tmp\</c> and the
        /// file's own name, **flat**. The folder it sits in inside the repository says nothing
        /// the import needs - where an object goes in TIA comes from <c>core.json</c> - and
        /// the names are unique across the whole core, which is measured rather than assumed
        /// and still guarded where the sources are brought down.
        /// </summary>
        /// <param name="repositoryPath">A node's <c>file</c>, relative to the repository root.</param>
        public static string SourceFor(string projectDirectory, string repositoryPath)
        {
            string tmp = TmpFor(projectDirectory);

            if (tmp == null || string.IsNullOrWhiteSpace(repositoryPath)) return null;

            string name = Path.GetFileName(repositoryPath.Replace('/', Path.DirectorySeparatorChar));

            return string.IsNullOrEmpty(name) ? null : Path.Combine(tmp, name);
        }

        /// <summary>The folder a project from before the change may still carry.</summary>
        internal static string RetiredCoreFor(string projectDirectory) => Under(projectDirectory, RetiredCoreFolder);

        private static string Under(string projectDirectory, string name)
        {
            string repo = For(projectDirectory);

            return repo == null ? null : Path.Combine(repo, name);
        }
    }
}
