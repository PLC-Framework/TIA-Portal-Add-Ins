using System;
using System.Collections.Generic;
using System.IO;

namespace Core.Repo
{
    /// <summary>
    /// Copies a core folder into the project's own <c>repo\core\</c>, and leaves it holding
    /// exactly what the source holds.
    ///
    /// **A mirror, not an addition, and that is a correctness property rather than tidiness.**
    /// A block deleted from the repository that survived here would be compared against, and
    /// the project would be told it is missing something the core no longer defines. So
    /// whatever the copy does not write, it removes.
    ///
    /// **It only ever removes inside the destination it was given**, and it refuses outright
    /// when the two folders overlap - a destination inside the source would have the copy
    /// feeding itself, and a source inside the destination would be deleted by the clean-up
    /// that follows. Both are one wrong path in a configuration away.
    /// </summary>
    public static class RepoCopy
    {
        /// <summary>
        /// Deep enough for any repository laid out by hand, and a guarantee that a junction
        /// pointing back up its own tree cannot spin forever.
        /// </summary>
        private const int MaxDepth = 32;

        /// <summary>
        /// Makes <paramref name="destination"/> hold what <paramref name="source"/> holds.
        /// **Never throws**: a file that could not be copied is one line in the result, and
        /// the rest of the core still arrives.
        /// </summary>
        public static RepoCopyResult Mirror(string source, string destination)
        {
            if (string.IsNullOrWhiteSpace(source)) return RepoCopyResult.Refused("No core folder was given.");
            if (string.IsNullOrWhiteSpace(destination)) return RepoCopyResult.Refused("There is nowhere to copy to.");

            string from, to;
            try
            {
                from = Path.GetFullPath(source);
                to = Path.GetFullPath(destination);
            }
            catch (Exception exception)
            {
                return RepoCopyResult.Refused("The paths could not be read: " + exception.Message);
            }

            if (!Directory.Exists(from))
                return RepoCopyResult.Refused("The core folder does not exist: " + from);

            if (Same(from, to))
                return RepoCopyResult.Refused("The core folder and the copy are the same folder: " + from);

            if (Inside(to, from))
                return RepoCopyResult.Refused("The copy would sit inside the core folder it is copying: " + to);

            if (Inside(from, to))
                return RepoCopyResult.Refused("The core folder sits inside the copy, which would delete it: " + from);

            List<string> problems = new List<string>();
            HashSet<string> written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                Directory.CreateDirectory(to);
            }
            catch (Exception exception)
            {
                return RepoCopyResult.Refused("The copy folder could not be created: " + exception.Message);
            }

            int files = Copy(from, to, 0, written, problems);
            int removed = Clean(to, written, problems);

            return RepoCopyResult.Done(to, files, removed, problems);
        }

        private static int Copy(string from, string to, int depth, HashSet<string> written, List<string> problems)
        {
            if (depth >= MaxDepth)
            {
                problems.Add("Stopped at " + MaxDepth + " folders deep: " + from);
                return 0;
            }

            int files = 0;

            string[] entries;
            try
            {
                entries = Directory.GetFiles(from);
            }
            catch (Exception exception)
            {
                problems.Add(from + ": " + exception.Message);
                return 0;
            }

            foreach (string file in entries)
            {
                string target = Path.Combine(to, Path.GetFileName(file));

                try
                {
                    File.Copy(file, target, true);

                    // A file copied off a read-only checkout arrives read-only, and the next
                    // mirror could then neither overwrite nor delete it.
                    File.SetAttributes(target, FileAttributes.Normal);

                    written.Add(target);
                    files++;
                }
                catch (Exception exception)
                {
                    problems.Add(Path.GetFileName(file) + ": " + exception.Message);
                }
            }

            string[] folders;
            try
            {
                folders = Directory.GetDirectories(from);
            }
            catch (Exception exception)
            {
                problems.Add(from + ": " + exception.Message);
                return files;
            }

            foreach (string folder in folders)
            {
                string target = Path.Combine(to, Path.GetFileName(folder));

                try
                {
                    Directory.CreateDirectory(target);
                }
                catch (Exception exception)
                {
                    problems.Add(Path.GetFileName(folder) + ": " + exception.Message);
                    continue;
                }

                files += Copy(folder, target, depth + 1, written, problems);
            }

            return files;
        }

        /// <summary>
        /// Removes what this run did not write. Empty folders go with their files, because a
        /// folder emptied by a removal says nothing and reads as a family the core still has.
        /// </summary>
        private static int Clean(string folder, HashSet<string> written, List<string> problems)
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
                if (written.Contains(file)) continue;

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
                removed += Clean(child, written, problems);

                try
                {
                    if (Directory.GetFileSystemEntries(child).Length == 0) Directory.Delete(child);
                }
                catch (Exception)
                {
                    // A folder that will not go is not worth a line of its own: it is empty,
                    // it holds nothing that can be mistaken for the core, and the next mirror
                    // will try again.
                }
            }

            return removed;
        }

        private static bool Same(string left, string right) =>
            string.Equals(Ending(left), Ending(right), StringComparison.OrdinalIgnoreCase);

        /// <summary>Whether <paramref name="inner"/> sits under <paramref name="outer"/>.</summary>
        private static bool Inside(string inner, string outer) =>
            Ending(inner).StartsWith(Ending(outer), StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// A path that ends in a separator, so a prefix test cannot read <c>C:\coreX</c> as
        /// sitting inside <c>C:\core</c>.
        /// </summary>
        private static string Ending(string path) =>
            path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
    }
}
