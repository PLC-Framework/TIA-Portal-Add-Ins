using System;
using System.IO;

namespace Core
{
    /// <summary>
    /// Writes a file so that whoever reads it finds the previous one or the new one, and never
    /// half of either.
    ///
    /// **Written beside the destination and swapped in.** A write that fails half way - a full
    /// disk, a dropped connection, a process that is killed - leaves the destination as it was,
    /// and the half-written file carries a suffix nothing reads. That matters here because what
    /// is left behind is read as though it were whole: a truncated <c>project.json</c> is a map
    /// with holes in it, and a truncated <c>.scl</c> is imported as the block.
    ///
    /// **`File.Replace`, and a delete followed by a move only where the volume refuses it.** The
    /// three writers in <c>Repo\</c> each spelled this out until 2026-09-22 as a delete and then a
    /// move, which leaves a moment with no file at all - a crash there loses the previous copy
    /// their own comments promised to keep. `File.Replace` swaps the two in one call; a volume
    /// that will not do it falls back to the old order rather than failing a write that would
    /// have worked the day before.
    ///
    /// **One temporary name per destination, not a fresh one per write**, so a write that was
    /// killed before it could clean up is overwritten by the next one rather than collecting
    /// beside it.
    /// </summary>
    internal static class SafeFile
    {
        private const string Suffix = ".writing";

        /// <summary>
        /// Has <paramref name="fill"/> write the new content to a temporary path beside
        /// <paramref name="path"/>, then puts it in place. **Throws what the write throws**, so a
        /// caller keeps turning failures into its own sentences; the temporary never survives.
        /// </summary>
        internal static void Write(string path, Action<string> fill)
        {
            string temporary = path + Suffix;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));

                fill(temporary);

                Swap(temporary, path);

                temporary = null;
            }
            finally
            {
                Discard(temporary);
            }
        }

        /// <summary>Writes <paramref name="content"/> to <paramref name="path"/>.</summary>
        internal static void WriteBytes(string path, byte[] content) =>
            Write(path, temporary => File.WriteAllBytes(temporary, content));

        /// <summary>
        /// Copies <paramref name="from"/> to <paramref name="path"/>. The copy is made writable:
        /// off a read-only checkout it arrives read-only, and then neither the next write nor a
        /// clean-up could replace or remove it.
        /// </summary>
        internal static void Copy(string from, string path) =>
            Write(path, temporary =>
            {
                File.Copy(from, temporary, true);
                File.SetAttributes(temporary, FileAttributes.Normal);
            });

        private static void Swap(string temporary, string path)
        {
            if (!File.Exists(path))
            {
                File.Move(temporary, path);
                return;
            }

            // A read-only destination refuses Replace as surely as it refuses Delete.
            File.SetAttributes(path, FileAttributes.Normal);

            try
            {
                File.Replace(temporary, path, null, true);
            }
            catch (Exception exception) when (exception is IOException || exception is PlatformNotSupportedException)
            {
                // A volume that does not do Replace - FAT, some network redirectors. Only fall
                // back while the new content is still whole under its temporary name; if Replace
                // got far enough to take it, what it says is the answer.
                if (!File.Exists(temporary)) throw;

                if (File.Exists(path)) File.Delete(path);

                File.Move(temporary, path);
            }
        }

        private static void Discard(string temporary)
        {
            try
            {
                if (temporary != null && File.Exists(temporary)) File.Delete(temporary);
            }
            catch (Exception)
            {
                // It carries the suffix, so nothing reads it; the next write to the same
                // destination overwrites it.
            }
        }
    }
}
