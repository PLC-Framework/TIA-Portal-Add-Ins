using System;
using System.IO;
using System.Text;
using System.Threading;

namespace Core.Logging
{
    /// <summary>
    /// The bytes: how a run's text reaches the file, and how the file is kept from growing
    /// without end. Nothing above this has to know either.
    ///
    /// **A shared handle loses lines, and that was measured rather than reasoned about**
    /// (2026-09-24). `FileMode.Append` on .NET Framework seeks to the end when it *opens* and
    /// then writes from its own cached position, so two processes appending to one file
    /// overwrite each other. Two writers of 2,000 lines each:
    ///
    ///     one handle held open, FileShare.ReadWrite    2,000 of 4,000 survived
    ///     opened per line, FileShare.ReadWrite         2,617 of 4,000 survived
    ///     opened per line, exclusive, retrying         4,000 of 4,000 survived
    ///
    /// **and the loss is silent**: no exception, no half-written line, a file that looks
    /// perfectly well-formed with a third of it missing. So the file is taken *exclusively* for
    /// the length of one write, readers still allowed, and a writer that finds it held waits.
    /// It costs about 0.1 ms a line, which a walk of four hundred objects does not notice.
    ///
    /// **The first run of that measurement said 4,000 of 4,000 for the shared handle**, and it
    /// was wrong: starting a process takes longer than writing 300 lines, so the two never
    /// overlapped. Both writers are released from one gate now. The naive version of this
    /// measurement lies, exactly as the naive screen capture does.
    ///
    /// **One call writes one block, so a block cannot be interleaved.** That is what lets an
    /// exception carry its stack over several lines without another process landing in the
    /// middle of it.
    /// </summary>
    internal static class LogFile
    {
        /// <summary>
        /// How long a writer waits for one held by somebody else, in milliseconds. Long enough
        /// to outlast another process writing a line, short enough that a log can never be
        /// what makes an application look stopped.
        /// </summary>
        private const int Budget = 250;

        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        /// <summary>
        /// Appends <paramref name="text"/> whole, or answers false having written nothing.
        ///
        /// **It never throws**, which is the rule the credential store and the `.env` reader
        /// already follow: no permission, no folder, a read-only file or a full disk all mean
        /// the line is lost, never that the run is. What is lost is counted by the caller and
        /// said out loud when the log closes.
        /// </summary>
        internal static bool Append(string path, string text)
        {
            int started = Environment.TickCount;

            while (true)
            {
                try
                {
                    // FileShare.Read, not ReadWrite: another writer is what must be kept out,
                    // while somebody tailing the file is welcome.
                    using (FileStream stream = new FileStream(
                        path, FileMode.Append, FileAccess.Write, FileShare.Read))
                    using (StreamWriter writer = new StreamWriter(stream, Utf8))
                    {
                        writer.Write(text);
                    }

                    return true;
                }
                catch (IOException)
                {
                    // Somebody else has it. TickCount rather than a Stopwatch because it is
                    // one of the things measured to survive the tightest partial trust.
                    if (unchecked(Environment.TickCount - started) > Budget) return false;

                    Thread.Sleep(1);
                }
                catch (Exception)
                {
                    // Denied, missing, read-only: this log has nothing more to say, and says
                    // it by going quiet rather than by taking the caller down with it.
                    return false;
                }
            }
        }

        /// <summary>
        /// Rolls the file behind its previous copy once it passes <paramref name="limit"/>.
        ///
        /// **Best effort, and silent about failing.** Two processes can arrive here together
        /// and one of them finds the file already moved; a file somebody has open cannot be
        /// moved at all. Neither is a reason to stop writing - the worst of it is a log that
        /// grows past a megabyte until the next run gets the chance.
        /// </summary>
        internal static void Roll(string path, string previous, long limit)
        {
            try
            {
                FileInfo file = new FileInfo(path);

                if (!file.Exists || file.Length < limit) return;

                if (File.Exists(previous)) File.Delete(previous);

                File.Move(path, previous);
            }
            catch (Exception)
            {
            }
        }
    }
}
