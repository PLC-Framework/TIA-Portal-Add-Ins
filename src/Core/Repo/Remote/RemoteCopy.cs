using System;
using System.Collections.Generic;
using System.IO;

// The hash is git's rather than this layer's, and it is what the listing gives: see the note
// in `GitBlobSha`. A remote that was not a git host would answer some other identifier, and
// this file would be the one to learn it.
using Core.Repo.GitHub;

namespace Core.Repo.Remote
{
    /// <summary>
    /// Brings a project's <c>repo\core\</c> up to date from a repository that is not on this
    /// machine, and leaves it holding exactly what that repository holds.
    ///
    /// **The same contract as <see cref="RepoCopy"/>, over a wire instead of a folder**: what
    /// the source does not list is removed, because a block retired from the core that
    /// survived in the copy would be compared against and the project told it is missing
    /// something the core no longer defines.
    ///
    /// **What is different is that every file costs a request**, so this one asks before it
    /// fetches. A file whose content hash already matches is left alone, which makes a second
    /// run download nothing at all - and the hash is computed from the copy rather than kept
    /// in an index, so a file somebody edited, truncated or replaced is repaired rather than
    /// trusted. See <see cref="GitBlobSha"/>.
    ///
    /// **Nothing is written in place.** Each file lands in <c>repo\tmp\</c> and is moved over
    /// its destination, so a connection that drops mid-file cannot leave half an `.scl` behind
    /// looking like a block.
    ///
    /// **It never throws.** A file that would not come down is one line in the result and the
    /// rest of the core still arrives - but the run is not `Ready`, because a core with a hole
    /// in it must not be compared against as though it were whole.
    /// </summary>
    public static class RemoteCopy
    {
        /// <param name="files">The repository, already open.</param>
        /// <param name="folder">The core's path inside it, <c>plc/s7-1x00/core</c>.</param>
        /// <param name="into">The project's <c>repo\core\</c>.</param>
        /// <param name="scratch">The project's <c>repo\tmp\</c>, where a file lands first.</param>
        /// <param name="progress">Told how far along it is, because this can take a minute.</param>
        public static RemoteCopyResult Mirror(
            IRemoteFiles files, string folder, string into, string scratch, Action<string> progress)
        {
            if (files == null) return RemoteCopyResult.Refused("There is no way to reach the repository from here.");
            if (string.IsNullOrWhiteSpace(folder)) return RemoteCopyResult.Refused("The core's folder in the repository is empty.");
            if (string.IsNullOrWhiteSpace(into)) return RemoteCopyResult.Refused("There is nowhere to copy the core to.");

            string commit;
            IReadOnlyList<RemoteFile> listed;

            try
            {
                progress?.Invoke("Asking the repository what it is at…");

                commit = files.Head();

                progress?.Invoke("Reading the repository's file list…");

                listed = files.Under(commit, folder) ?? new RemoteFile[0];
            }
            catch (Exception exception)
            {
                // The four refusals the client tells apart - no token, no such repository, the
                // rate limit, no network - arrive here as one sentence each, already written
                // for somebody to act on.
                return RemoteCopyResult.Refused(exception.Message);
            }

            if (listed.Count == 0)
                return RemoteCopyResult.Refused(
                    "The repository has nothing under '" + folder + "' at " + Short(commit) + ".");

            string destination;
            try
            {
                destination = Path.GetFullPath(into);
                Directory.CreateDirectory(destination);
            }
            catch (Exception exception)
            {
                return RemoteCopyResult.Refused("The copy folder could not be made: " + exception.Message);
            }

            string prefix = folder.Trim().Replace('\\', '/').Trim('/') + "/";

            List<string> problems = new List<string>();
            HashSet<string> wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            int downloaded = 0;
            int kept = 0;
            int done = 0;

            foreach (RemoteFile file in listed)
            {
                done++;

                string relative = Relative(file.Path, prefix);

                if (relative == null)
                {
                    // The listing answered something outside the folder it was asked about.
                    // Not this layer's to fix, and not something to write either.
                    problems.Add(file.Path + " is not inside '" + folder + "'.");
                    continue;
                }

                string target = Path.Combine(destination, relative.Replace('/', Path.DirectorySeparatorChar));

                wanted.Add(target);

                if (string.Equals(GitBlobSha.OfFile(target), file.Hash, StringComparison.OrdinalIgnoreCase))
                {
                    kept++;
                    continue;
                }

                progress?.Invoke("Downloading " + done + " of " + listed.Count + " - " + Path.GetFileName(relative));

                string problem = Fetch(files, file, target, scratch);

                if (problem == null) downloaded++;
                else problems.Add(Path.GetFileName(relative) + ": " + problem);
            }

            int removed = Clean(destination, wanted, problems);

            return RemoteCopyResult.Done(destination, commit, downloaded, kept, removed, problems);
        }

        /// <summary>
        /// One file, through <c>repo\tmp\</c> and then over its destination.
        ///
        /// **Written under a name of this run's own**, because two files of the same name in
        /// different folders of the core would otherwise share a scratch file - and a move
        /// that failed would leave the wrong bytes waiting for the next one.
        /// </summary>
        private static string Fetch(IRemoteFiles files, RemoteFile file, string target, string scratch)
        {
            string temporary = null;

            try
            {
                byte[] content = files.Read(file.Hash);

                if (content == null) return "the repository returned nothing.";

                // Held against what was asked for rather than taken on trust: a proxy or a
                // truncated response would otherwise be written into the core as the block.
                string got = GitBlobSha.Of(content);

                if (!string.Equals(got, file.Hash, StringComparison.OrdinalIgnoreCase))
                    return "what came back is not what was asked for (" + Short(got) + " instead of " +
                           Short(file.Hash) + ").";

                Directory.CreateDirectory(Path.GetDirectoryName(target));

                temporary = Temporary(scratch, target, file.Hash);

                File.WriteAllBytes(temporary, content);

                if (File.Exists(target))
                {
                    // A file copied off a read-only checkout, or restored from one, arrives
                    // read-only - and then neither goes nor is written over.
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

        /// <summary>
        /// A scratch path for one file. Falls back to beside the destination when there is no
        /// <c>tmp\</c> to write in: the move is then within one folder, which is what makes it
        /// atomic, and that matters more than where the file waited.
        /// </summary>
        private static string Temporary(string scratch, string target, string hash)
        {
            string name = Path.GetFileName(target) + "." + Short(hash) + ".downloading";

            if (string.IsNullOrWhiteSpace(scratch)) return Path.Combine(Path.GetDirectoryName(target), name);

            try
            {
                Directory.CreateDirectory(scratch);

                return Path.Combine(scratch, name);
            }
            catch (Exception)
            {
                return Path.Combine(Path.GetDirectoryName(target), name);
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

        /// <summary>
        /// Removes what the repository no longer lists, and the folders that empties.
        ///
        /// **Only inside the copy**, which is a folder this framework made and owns - the
        /// same rule the local mirror follows, and the reason that one refuses a destination
        /// overlapping its source.
        /// </summary>
        private static int Clean(string folder, HashSet<string> wanted, List<string> problems)
        {
            int removed = 0;

            string[] files;
            try
            {
                files = Directory.GetFiles(folder);
            }
            catch (Exception exception)
            {
                problems.Add(folder + ": " + exception.Message);
                return 0;
            }

            foreach (string file in files)
            {
                if (wanted.Contains(file)) continue;

                try
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                    File.Delete(file);
                    removed++;
                }
                catch (Exception exception)
                {
                    problems.Add(Path.GetFileName(file) + " could not be removed: " + exception.Message);
                }
            }

            string[] folders;
            try
            {
                folders = Directory.GetDirectories(folder);
            }
            catch (Exception exception)
            {
                problems.Add(folder + ": " + exception.Message);
                return removed;
            }

            foreach (string child in folders)
            {
                removed += Clean(child, wanted, problems);

                try
                {
                    if (Directory.GetFileSystemEntries(child).Length == 0) Directory.Delete(child);
                }
                catch (Exception)
                {
                    // Empty, holding nothing that can be mistaken for the core, and the next
                    // run tries again.
                }
            }

            return removed;
        }

        /// <summary>
        /// A listed path with the core's folder taken off it, or null when it is not under it.
        /// **Case-sensitively**, because git keeps two paths differing only in case apart.
        /// </summary>
        private static string Relative(string path, string prefix)
        {
            if (string.IsNullOrEmpty(path)) return null;

            string normalised = path.Replace('\\', '/').TrimStart('/');

            if (!normalised.StartsWith(prefix, StringComparison.Ordinal)) return null;

            string relative = normalised.Substring(prefix.Length);

            return relative.Length == 0 ? null : relative;
        }

        private static string Short(string hash) =>
            string.IsNullOrEmpty(hash) ? "?" : (hash.Length <= 7 ? hash : hash.Substring(0, 7));
    }
}
