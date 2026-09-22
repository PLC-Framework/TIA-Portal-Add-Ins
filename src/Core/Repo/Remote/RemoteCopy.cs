using System;
using System.Collections.Generic;
using System.IO;

using Core.Repo.Local;

// The hash is git's rather than this layer's or any one host's, and it is what the listing
// gives: see the note in `GitBlobSha`. Every git host answers the same blob id, so a second
// provider costs nothing here - a remote that was not a git host at all would answer some other
// identifier, and this file is the one that would have to learn it.
using Core.Repo.Git;

namespace Core.Repo.Remote
{
    /// <summary>
    /// Brings what a project needs out of a core that lives in a repository nobody here hosts:
    /// its graph when the project is loaded, and a handful of its sources when something is
    /// about to import them.
    ///
    /// **It used to mirror the whole core folder**, and that stopped on 2026-09-22 (the
    /// maintainer's decision). The comparison reads one file, and every other file cost a
    /// request - 277 on a first Load, on an hourly budget of sixty without a token, which is to
    /// say a public core could not be loaded at all. Now a Load is the branch, the listing and
    /// the graph; a download fetches exactly the sources it is about to import.
    ///
    /// **What the mirror got right is kept.** A file whose content hash already matches is not
    /// fetched again, which is what makes a Load whose core has not moved cost two requests and
    /// no download; bytes are held against the hash that was asked for before they are written;
    /// and nothing lands under its real name until it is whole.
    ///
    /// **It always asks for the branch's head**, the maintainer's rule: the repository is the
    /// source of truth, and an import brings what the core holds now. A source the head no longer
    /// lists is refused by name rather than fetched from an older commit.
    ///
    /// **It never throws.** A file that would not come down is one line in the result; whoever
    /// asked decides what a result with a hole in it is worth - and for both callers the answer
    /// is nothing.
    /// </summary>
    public static class RemoteCopy
    {
        /// <summary>
        /// Brings the core's graph to <paramref name="into"/>, the project's <c>repo\core.json</c>,
        /// unless the copy already there is the one the repository holds.
        /// </summary>
        /// <param name="folder">The core's path inside the repository, <c>plc/s7-1x00/core</c>.</param>
        /// <param name="dependencyFile">What the repository calls the graph, today <c>core.json</c>.</param>
        public static RemoteCopyResult Graph(
            IRemoteFiles files, string folder, string dependencyFile, string into, Action<string> progress)
        {
            if (string.IsNullOrWhiteSpace(dependencyFile))
                return RemoteCopyResult.Refused("The configuration names no dependency file.");

            if (string.IsNullOrWhiteSpace(into))
                return RemoteCopyResult.Refused("There is nowhere to copy the core to.");

            string wanted = Prefix(folder) + dependencyFile.Trim().Replace('\\', '/').TrimStart('/');

            string commit;
            Dictionary<string, RemoteFile> listed;
            string refused = List(files, folder, progress, out commit, out listed);

            if (refused != null) return RemoteCopyResult.Refused(refused);

            RemoteFile graph;
            if (!listed.TryGetValue(wanted, out graph))
                return RemoteCopyResult.Refused(
                    "The repository has no '" + wanted + "' at " + Short(commit) + ".");

            if (Same(into, graph.Hash)) return RemoteCopyResult.Done(into, commit, 0, 1, null);

            progress?.Invoke("Downloading " + Path.GetFileName(wanted) + "…");

            string problem = Fetch(files, graph, into);

            return problem == null
                ? RemoteCopyResult.Done(into, commit, 1, 0, null)
                : RemoteCopyResult.Done(into, commit, 0, 0, new[] { Path.GetFileName(wanted) + ": " + problem });
        }

        /// <summary>
        /// Brings the named sources into <paramref name="into"/>, the project's <c>repo\tmp\</c>,
        /// each under its own file name and **flat** - the same rule, and the same refusal of two
        /// sources sharing a name, as <see cref="LocalCopy.Sources"/>.
        /// </summary>
        /// <param name="repositoryPaths">Nodes' <c>file</c>s, relative to the repository root.</param>
        public static RemoteCopyResult Sources(
            IRemoteFiles files, string folder, IEnumerable<string> repositoryPaths, string into, Action<string> progress)
        {
            if (string.IsNullOrWhiteSpace(into)) return RemoteCopyResult.Refused("There is nowhere to put the sources.");

            try
            {
                Directory.CreateDirectory(into);
            }
            catch (Exception exception)
            {
                return RemoteCopyResult.Refused("The folder for the sources could not be made: " + exception.Message);
            }

            List<string> problems = new List<string>();
            Dictionary<string, string> named = LocalCopy.Flat(repositoryPaths, problems);

            // Nothing left to fetch costs nothing: the head and the listing are two requests out
            // of an hourly budget, and asking them to bring no file would be spending it on habit.
            if (named.Count == 0) return RemoteCopyResult.Done(into, null, 0, 0, problems);

            string commit;
            Dictionary<string, RemoteFile> listed;
            string refused = List(files, folder, progress, out commit, out listed);

            if (refused != null) return RemoteCopyResult.Refused(refused);

            int downloaded = 0;
            int kept = 0;
            int done = 0;

            foreach (KeyValuePair<string, string> one in named)
            {
                done++;

                RemoteFile file;
                if (!listed.TryGetValue(one.Value, out file))
                {
                    // The head no longer lists it: renamed, retired, or moved since the Load
                    // that planned this. Fetching it from an older commit would import a core
                    // the repository has stopped standing behind.
                    problems.Add(one.Key + " is no longer in the core at " + Short(commit) + ".");
                    continue;
                }

                string target = Path.Combine(into, one.Key);

                if (Same(target, file.Hash))
                {
                    kept++;
                    continue;
                }

                progress?.Invoke("Downloading " + done + " of " + named.Count + " - " + one.Key);

                string problem = Fetch(files, file, target);

                if (problem == null) downloaded++;
                else problems.Add(one.Key + ": " + problem);
            }

            return RemoteCopyResult.Done(into, commit, downloaded, kept, problems);
        }

        /// <summary>
        /// The branch's head and what it lists under the core folder, by full repository path.
        /// </summary>
        private static string List(
            IRemoteFiles files, string folder, Action<string> progress,
            out string commit, out Dictionary<string, RemoteFile> listed)
        {
            commit = null;
            listed = new Dictionary<string, RemoteFile>(StringComparer.Ordinal);

            if (files == null) return "There is no way to reach the repository from here.";
            if (string.IsNullOrWhiteSpace(folder)) return "The core's folder in the repository is empty.";

            try
            {
                progress?.Invoke("Asking the repository what it is at…");

                commit = files.Head();

                progress?.Invoke("Reading the repository's file list…");

                foreach (RemoteFile file in files.Under(commit, folder) ?? new RemoteFile[0])
                    if (file != null && !string.IsNullOrEmpty(file.Path))
                        listed[file.Path.Replace('\\', '/').TrimStart('/')] = file;

                return null;
            }
            catch (Exception exception)
            {
                // The refusals the client tells apart - no token, no such repository, a move,
                // the rate limit, no network - arrive here as one sentence each, already written
                // for somebody to act on.
                return exception.Message;
            }
        }

        /// <summary>Whether the file already on disk is the one the repository holds.</summary>
        private static bool Same(string path, string hash) =>
            string.Equals(GitBlobSha.OfFile(path), hash, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// One file, written beside its destination and moved over it, so a connection that drops
        /// mid-file cannot leave half an <c>.scl</c> under a name an import will read.
        /// </summary>
        private static string Fetch(IRemoteFiles files, RemoteFile file, string target)
        {
            string temporary = target + "." + Short(file.Hash) + ".downloading";

            try
            {
                byte[] content = files.Read(file.Hash);

                if (content == null) return "the repository returned nothing.";

                // Held against what was asked for rather than taken on trust: a proxy or a
                // truncated response would otherwise be imported as the block.
                string got = GitBlobSha.Of(content);

                if (!string.Equals(got, file.Hash, StringComparison.OrdinalIgnoreCase))
                    return "what came back is not what was asked for (" + Short(got) + " instead of " +
                           Short(file.Hash) + ").";

                Directory.CreateDirectory(Path.GetDirectoryName(target));

                File.WriteAllBytes(temporary, content);

                if (File.Exists(target))
                {
                    File.SetAttributes(target, FileAttributes.Normal);
                    File.Delete(target);
                }

                File.Move(temporary, target);
                temporary = null;

                return null;
            }
            catch (Exception exception)
            {
                return exception.Message;
            }
            finally
            {
                Discard(temporary);
            }
        }

        private static void Discard(string path)
        {
            try
            {
                if (path != null && File.Exists(path)) File.Delete(path);
            }
            catch (Exception)
            {
                // It carries a .downloading suffix, so nothing reads it as part of the core.
            }
        }

        private static string Prefix(string folder)
        {
            string trimmed = (folder ?? string.Empty).Trim().Replace('\\', '/').Trim('/');

            return trimmed.Length == 0 ? string.Empty : trimmed + "/";
        }

        private static string Short(string hash) =>
            string.IsNullOrEmpty(hash) ? "?" : (hash.Length <= 7 ? hash : hash.Substring(0, 7));
    }
}
