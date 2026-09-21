using System.Collections.Generic;

namespace Core.Repo.Remote
{
    /// <summary>
    /// What a remote mirror did: what it brought down, what it left alone, and why not.
    ///
    /// **It counts differently from its local twin, and that is the point of having two.**
    /// `LocalCopyResult` says how many files were copied, because a local mirror copies every
    /// one of them; this says how many came down the wire and how many were already right,
    /// which is the number an operator on a slow line actually wants. And it carries the
    /// commit, because that is the only name a remote core has that stays true.
    /// </summary>
    public sealed class RemoteCopyResult
    {
        private RemoteCopyResult(
            string folder, string commit, int downloaded, int kept, int removed,
            IReadOnlyList<string> problems, string refusal)
        {
            Folder = folder;
            Commit = commit;
            Downloaded = downloaded;
            Kept = kept;
            Removed = removed;
            Problems = problems ?? new string[0];
            Refusal = refusal;
        }

        /// <summary>The project's copy of the core.</summary>
        public string Folder { get; }

        /// <summary>The commit it now holds.</summary>
        public string Commit { get; }

        /// <summary>How many files came down the wire.</summary>
        public int Downloaded { get; }

        /// <summary>How many were already what the repository has.</summary>
        public int Kept { get; }

        /// <summary>How many the repository no longer lists.</summary>
        public int Removed { get; }

        /// <summary>Files that would not come down, one line each.</summary>
        public IReadOnlyList<string> Problems { get; }

        /// <summary>Why nothing happened at all, or null.</summary>
        public string Refusal { get; }

        public bool IsRefused => Refusal != null;

        /// <summary>
        /// Whether the copy is the core as the repository has it.
        ///
        /// **A file short is not "mostly done".** Everything past this reads the copy as the
        /// truth about what the core defines, so a hole in it would be read as the core not
        /// defining something - which is the difference between "you are missing a block" and
        /// "the download failed", told the wrong way round.
        /// </summary>
        public bool Ready => !IsRefused && Problems.Count == 0;

        public static RemoteCopyResult Refused(string why) =>
            new RemoteCopyResult(null, null, 0, 0, 0, null, why ?? "The core could not be read.");

        public static RemoteCopyResult Done(
            string folder, string commit, int downloaded, int kept, int removed, IReadOnlyList<string> problems) =>
            new RemoteCopyResult(folder, commit, downloaded, kept, removed, problems, null);
    }
}
