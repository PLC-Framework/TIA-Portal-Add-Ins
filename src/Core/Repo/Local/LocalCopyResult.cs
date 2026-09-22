using System.Collections.Generic;

namespace Core.Repo.Local
{
    /// <summary>
    /// What came of copying out of a local core: how many files arrived, and anything that
    /// would not move.
    ///
    /// **Refused and partly-copied are different answers**, for the same reason the export
    /// keeps them apart: sources missing two files are not sources nothing was copied into, and
    /// importing from the first while believing it whole is how a project acquires a block
    /// whose dependency never arrived.
    /// </summary>
    public sealed class LocalCopyResult
    {
        private static readonly string[] Nothing = new string[0];

        private LocalCopyResult(string folder, int files, IReadOnlyList<string> problems, string refused)
        {
            Folder = folder;
            Files = files;
            Problems = problems ?? Nothing;
            Refusal = refused;
        }

        /// <summary>Where it was copied to, or null when nothing was attempted.</summary>
        public string Folder { get; }

        public int Files { get; }

        /// <summary>What would not copy, one line each. Empty when everything did.</summary>
        public IReadOnlyList<string> Problems { get; }

        /// <summary>Why nothing was copied at all, or null when something was.</summary>
        public string Refusal { get; }

        /// <summary>Nothing was copied.</summary>
        public bool IsRefused => Refusal != null;

        /// <summary>
        /// Everything that was asked for is here. **Named as its remote twin names it**,
        /// <see cref="Remote.RemoteCopyResult.Ready"/>: two results of one contract, read by the
        /// same callers, should not need two words for one fact. It was `IsComplete` until
        /// 2026-09-22.
        /// </summary>
        public bool Ready => !IsRefused && Problems.Count == 0;

        internal static LocalCopyResult Done(string folder, int files, IReadOnlyList<string> problems) =>
            new LocalCopyResult(folder, files, problems, null);

        internal static LocalCopyResult Refused(string refusal) =>
            new LocalCopyResult(null, 0, Nothing, refusal ?? "The core could not be copied.");
    }
}
