using System.Collections.Generic;

namespace Core.Repo.Remote
{
    /// <summary>
    /// What a trip to a remote repository did: what it brought down, what it found already
    /// right, and why not.
    ///
    /// **It counts differently from its local twin, and that is the point of having two.**
    /// `LocalCopyResult` says how many files were copied, because a folder on this machine costs
    /// nothing to read; this says how many came down the wire and how many were already right,
    /// which is the number an operator on a slow line wants. And it carries the commit, because
    /// that is the only name a remote core has that stays true.
    /// </summary>
    public sealed class RemoteCopyResult
    {
        private RemoteCopyResult(
            string folder, string commit, int downloaded, int kept, IReadOnlyList<string> problems, string refusal)
        {
            Folder = folder;
            Commit = commit;
            Downloaded = downloaded;
            Kept = kept;
            Problems = problems ?? new string[0];
            Refusal = refusal;
        }

        /// <summary>Where it was written: <c>repo\core.json</c>, or the <c>tmp\</c> folder.</summary>
        public string Folder { get; }

        /// <summary>The commit the branch was at, which is what was read.</summary>
        public string Commit { get; }

        /// <summary>How many files came down the wire.</summary>
        public int Downloaded { get; }

        /// <summary>How many were already what the repository has.</summary>
        public int Kept { get; }

        /// <summary>Files that would not come down, one line each.</summary>
        public IReadOnlyList<string> Problems { get; }

        /// <summary>Why nothing happened at all, or null.</summary>
        public string Refusal { get; }

        public bool IsRefused => Refusal != null;

        /// <summary>
        /// Whether everything asked for is here, as the repository has it.
        ///
        /// **A file short is not "mostly done".** A graph short of itself is no graph, and
        /// sources short of one are an import whose dependency never arrived - the object that
        /// needed it would fail to build, reported as the import's fault rather than the
        /// download's.
        /// </summary>
        public bool Ready => !IsRefused && Problems.Count == 0;

        public static RemoteCopyResult Refused(string why) =>
            new RemoteCopyResult(null, null, 0, 0, null, why ?? "The core could not be read.");

        public static RemoteCopyResult Done(
            string folder, string commit, int downloaded, int kept, IReadOnlyList<string> problems) =>
            new RemoteCopyResult(folder, commit, downloaded, kept, problems, null);
    }
}
