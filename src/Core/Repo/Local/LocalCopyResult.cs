using System.Collections.Generic;

namespace Core.Repo.Local
{
    /// <summary>
    /// What came of copying a core into the project: how much arrived, how much stale content
    /// went, and anything that would not move.
    ///
    /// **Refused and partly-copied are different answers**, for the same reason the export
    /// keeps them apart: a core missing four files is not a core nothing was copied into, and
    /// comparing against the first while believing it whole is how a report acquires a hole.
    /// </summary>
    public sealed class LocalCopyResult
    {
        private static readonly string[] Nothing = new string[0];

        private LocalCopyResult(string folder, int files, int removed, IReadOnlyList<string> problems, string refused)
        {
            Folder = folder;
            Files = files;
            Removed = removed;
            Problems = problems ?? Nothing;
            Refusal = refused;
        }

        /// <summary>Where the core was copied to, or null when nothing was attempted.</summary>
        public string Folder { get; }

        public int Files { get; }

        /// <summary>Files the copy removed because the core no longer has them.</summary>
        public int Removed { get; }

        /// <summary>What would not copy, one line each. Empty when everything did.</summary>
        public IReadOnlyList<string> Problems { get; }

        /// <summary>Why nothing was copied at all, or null when something was.</summary>
        public string Refusal { get; }

        /// <summary>Nothing was copied, so there is no core here to compare against.</summary>
        public bool IsRefused => Refusal != null;

        /// <summary>Everything the core holds is here.</summary>
        public bool IsComplete => !IsRefused && Problems.Count == 0;

        internal static LocalCopyResult Done(string folder, int files, int removed, IReadOnlyList<string> problems) =>
            new LocalCopyResult(folder, files, removed, problems, null);

        internal static LocalCopyResult Refused(string refusal) =>
            new LocalCopyResult(null, 0, 0, Nothing, refusal ?? "The core could not be copied.");
    }
}
