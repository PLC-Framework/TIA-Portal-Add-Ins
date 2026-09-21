using System;
using System.Collections.Generic;

namespace GitHubApi
{
    /// <summary>
    /// One file in a repository tree: where it is, what it is, and how big.
    ///
    /// **The sha is the point of this type.** It is the hash of the *content*, so two runs
    /// that answer the same sha for a path mean the file did not change - which is what lets
    /// a second download fetch nothing at all. It is also what the blob is asked for by.
    /// </summary>
    public sealed class GitHubFile
    {
        public GitHubFile(string path, string sha, long size)
        {
            Path = path;
            Sha = sha;
            Size = size;
        }

        /// <summary>The path from the repository root, with forward slashes, as GitHub gives it.</summary>
        public string Path { get; }

        /// <summary>The blob's sha - the content's hash, not the commit's.</summary>
        public string Sha { get; }

        public long Size { get; }

        public override string ToString() => Path + " (" + Size + " bytes)";
    }

    /// <summary>
    /// A repository tree at one commit: every file under it, and whether GitHub gave the
    /// whole of it.
    ///
    /// **Truncated is not a detail to step over.** The trees API stops at around a hundred
    /// thousand entries or seven megabytes of listing and says so in one field; a caller that
    /// ignores it gets a list that looks complete and is not, which for a core means blocks
    /// quietly missing from the comparison. Everything that reads this refuses rather than
    /// filters.
    /// </summary>
    public sealed class GitHubTree
    {
        public GitHubTree(string commit, IReadOnlyList<GitHubFile> files, bool truncated)
        {
            Commit = commit;
            Files = files;
            Truncated = truncated;
        }

        /// <summary>The commit this tree was read at.</summary>
        public string Commit { get; }

        public IReadOnlyList<GitHubFile> Files { get; }

        public bool Truncated { get; }

        /// <summary>
        /// The files under one folder, by prefix - <c>plc/s7-1x00/core</c> and everything
        /// below it.
        ///
        /// **A prefix match on a path needs the separator**, or `core` would also take
        /// `core-tools/`. And it is case-sensitive, because git is: two paths differing only
        /// in case are two files in a repository, whatever Windows would make of them
        /// afterwards.
        /// </summary>
        public IReadOnlyList<GitHubFile> Under(string folder)
        {
            if (Truncated)
                throw new GitHubException(
                    "GitHub returned only part of the repository's tree, so what is under '" +
                    folder + "' cannot be known. The repository is too large to list in one call.");

            List<GitHubFile> found = new List<GitHubFile>();

            if (string.IsNullOrWhiteSpace(folder)) return Files;

            string prefix = folder.Trim().Replace('\\', '/').Trim('/') + "/";

            foreach (GitHubFile file in Files)
                if (file.Path.StartsWith(prefix, StringComparison.Ordinal)) found.Add(file);

            return found;
        }
    }
}
