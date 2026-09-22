using System.Collections.Generic;

namespace Core.Repo.PlcCore
{
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
