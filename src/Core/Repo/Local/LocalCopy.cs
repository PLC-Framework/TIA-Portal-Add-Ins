using System;
using System.Collections.Generic;
using System.IO;

namespace Core.Repo.Local
{
    /// <summary>
    /// Brings what a project needs out of a core that is a folder on this machine: its graph
    /// when the project is loaded, and a handful of its sources when something is about to
    /// import them.
    ///
    /// **It used to mirror the whole core into <c>repo\core\</c>**, and that stopped on
    /// 2026-09-22 (the maintainer's decision). The comparison reads one file, <c>core.json</c>;
    /// the sources are read only by an import, and only the few it is importing. Copying the
    /// rest of the library into every project on every Load was work nobody used - and a project
    /// uses a handful of the core's libraries, never all of them.
    ///
    /// **The same two calls as <see cref="Remote.RemoteCopy"/>**, over a folder instead of a
    /// wire, so a download reads the same whichever half brought its sources.
    ///
    /// **Nothing here deletes anything**, which is what the old mirror had to guard so hard
    /// against - a destination overlapping its source could have had the clean-up delete the
    /// repository itself. A copy that only ever writes named files has nothing left to guard.
    /// </summary>
    public static class LocalCopy
    {
        /// <summary>
        /// Copies the core's graph to <paramref name="into"/>, the project's
        /// <c>repo\core.json</c>, through <see cref="SafeFile"/>: a copy that fails half way leaves
        /// the previous graph rather than a truncated one a comparison would read.
        /// </summary>
        public static LocalCopyResult Graph(LocalSource source, string into)
        {
            if (source == null || !source.Resolved)
                return LocalCopyResult.Refused(source?.Problem ?? "No repository was given.");

            if (string.IsNullOrWhiteSpace(into)) return LocalCopyResult.Refused("There is nowhere to copy the core to.");

            try
            {
                if (!File.Exists(source.GraphFile))
                    return LocalCopyResult.Refused("The dependency file does not exist: " + source.GraphFile);

                SafeFile.Copy(source.GraphFile, into);

                return LocalCopyResult.Done(into, 1, null);
            }
            catch (Exception exception)
            {
                return LocalCopyResult.Refused("The core's graph could not be copied: " + exception.Message);
            }
        }

        /// <summary>
        /// Copies the named sources into <paramref name="into"/>, the project's <c>repo\tmp\</c>,
        /// each under its own file name and **flat**.
        ///
        /// **Two sources with one file name are refused, not overwritten.** Every name is unique
        /// in today's core, measured; but that is a fact about one core, and the second copy
        /// landing on the first would import one block's source as another's with nothing saying
        /// so. Neither is copied, and the problem names both.
        ///
        /// **Never throws**: a file that would not copy is a line in the result, and whoever
        /// asked decides whether an import with a hole in it goes ahead - which it should not.
        /// </summary>
        /// <param name="repositoryPaths">Nodes' <c>file</c>s, relative to the repository root.</param>
        public static LocalCopyResult Sources(LocalSource source, IEnumerable<string> repositoryPaths, string into)
        {
            if (source == null || !source.Resolved)
                return LocalCopyResult.Refused(source?.Problem ?? "No repository was given.");

            if (string.IsNullOrWhiteSpace(into)) return LocalCopyResult.Refused("There is nowhere to put the sources.");

            try
            {
                Directory.CreateDirectory(into);
            }
            catch (Exception exception)
            {
                return LocalCopyResult.Refused("The folder for the sources could not be made: " + exception.Message);
            }

            List<string> problems = new List<string>();
            Dictionary<string, string> named = Flat(repositoryPaths, problems);
            int files = 0;

            foreach (KeyValuePair<string, string> one in named)
            {
                string from = Path.Combine(source.Root, one.Value.Replace('/', Path.DirectorySeparatorChar));
                string target = Path.Combine(into, one.Key);

                try
                {
                    if (!File.Exists(from))
                    {
                        problems.Add(one.Key + " is not in the repository: " + from);
                        continue;
                    }

                    // Swapped in whole, so an import never reads half a source; made writable
                    // on the way, since off a read-only checkout it arrives read-only and then
                    // neither the next download nor the clean-up after this one could touch it.
                    SafeFile.Copy(from, target);

                    files++;
                }
                catch (Exception exception)
                {
                    problems.Add(one.Key + ": " + exception.Message);
                }
            }

            return LocalCopyResult.Done(into, files, problems);
        }

        /// <summary>
        /// The file names a set of repository paths would land under, each once. A name two paths
        /// share is reported and dropped from both.
        /// </summary>
        internal static Dictionary<string, string> Flat(IEnumerable<string> repositoryPaths, List<string> problems)
        {
            Dictionary<string, string> named = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> clashed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string path in repositoryPaths ?? new string[0])
            {
                if (string.IsNullOrWhiteSpace(path)) continue;

                string normal = path.Replace('\\', '/').TrimStart('/');
                string name = Path.GetFileName(normal.Replace('/', Path.DirectorySeparatorChar));

                if (string.IsNullOrEmpty(name)) continue;

                string earlier;
                if (named.TryGetValue(name, out earlier))
                {
                    if (string.Equals(earlier, normal, StringComparison.OrdinalIgnoreCase)) continue;

                    problems.Add("Two sources are both called '" + name + "' - " + earlier + " and " + normal +
                                 " - so neither is brought down: one would land on the other.");
                    clashed.Add(name);
                    continue;
                }

                named.Add(name, normal);
            }

            foreach (string name in clashed) named.Remove(name);

            return named;
        }
    }
}
