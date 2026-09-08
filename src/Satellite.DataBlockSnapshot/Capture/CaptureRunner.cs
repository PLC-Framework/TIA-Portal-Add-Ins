using System;
using System.Collections.Generic;
using System.Threading;

using S7PlcWebserverApi;

using Satellite.DataBlockSnapshot.Export;

namespace Satellite.DataBlockSnapshot.Capture
{
    /// <summary>How one data block ended up.</summary>
    public enum CaptureOutcome
    {
        Pending,
        Working,
        Written,
        Missing,
        Failed,
        Cancelled
    }

    /// <summary>Progress for one block, as the window shows it.</summary>
    public sealed class CaptureProgress
    {
        public CaptureProgress(string dataBlock, CaptureOutcome outcome, string detail)
        {
            DataBlock = dataBlock;
            Outcome = outcome;
            Detail = detail;
        }

        public string DataBlock { get; }
        public CaptureOutcome Outcome { get; }
        public string Detail { get; }
    }

    public sealed class CaptureSettings
    {
        public string Address { get; set; }
        public string User { get; set; }
        public string Password { get; set; }
        public string Folder { get; set; }
        public TimeSpan Timeout { get; set; }
        public IReadOnlyList<string> DataBlocks { get; set; }
    }

    /// <summary>
    /// Reads the chosen data blocks and writes one workbook per block.
    ///
    /// Synchronous, and meant to be called from a background thread: PlcClient blocks,
    /// and a capture runs for tens of seconds. Progress comes back through IProgress, which
    /// marshals to the UI thread on its own.
    /// </summary>
    public static class CaptureRunner
    {
        /// <param name="authenticated">
        /// Called once, on this thread, the moment the CPU has accepted the credentials -
        /// and never if it has not. That distinction is the whole point: it is what lets
        /// the caller remember a password only when it is known to be the right one. The
        /// runner itself stays ignorant of what the caller does with the news.
        /// </param>
        public static void Run(
            CaptureSettings settings,
            IProgress<CaptureProgress> progress,
            CancellationToken cancellation,
            Action authenticated = null)
        {
            using (PlcClient client = new PlcClient(
                       settings.Address, settings.User, settings.Password, settings.Timeout))
            {
                client.Login();
                authenticated?.Invoke();

                // One call, before anything long starts. The block names came out of a TIA
                // project and the operator may be pointing at a different CPU entirely;
                // saying so up front beats discovering it one failed block at a time.
                HashSet<string> present = new HashSet<string>(
                    client.ListDataBlocks(), StringComparer.OrdinalIgnoreCase);

                foreach (string block in settings.DataBlocks)
                {
                    // Cancellation is checked between blocks. Inside one, the work is a
                    // browse and a batched read that finish in seconds.
                    if (cancellation.IsCancellationRequested)
                    {
                        progress?.Report(new CaptureProgress(block, CaptureOutcome.Cancelled, "Cancelled."));
                        continue;
                    }

                    if (!present.Contains(block))
                    {
                        progress?.Report(new CaptureProgress(
                            block, CaptureOutcome.Missing, "Not present on this CPU."));
                        continue;
                    }

                    CaptureOne(client, settings, block, progress);
                }
            }
        }

        private static void CaptureOne(
            PlcClient client, CaptureSettings settings, string block, IProgress<CaptureProgress> progress)
        {
            progress?.Report(new CaptureProgress(block, CaptureOutcome.Working, "Reading..."));

            try
            {
                DateTime started = DateTime.Now;

                BrowseResult browse = client.BrowseDb(block);
                if (browse.Variables.Count == 0)
                {
                    progress?.Report(new CaptureProgress(
                        block, CaptureOutcome.Failed, "The block holds no readable variables."));
                    return;
                }

                List<string> paths = new List<string>(browse.Variables.Count);
                foreach (PlcVariable variable in browse.Variables) paths.Add(variable.Path);

                IReadOnlyList<PlcValue> values = client.Read(paths);
                DateTime finished = DateTime.Now;

                Snapshot snapshot = Snapshot.Create(
                    settings.Address, block, started, finished, browse, values);

                // Nothing is written unless the block was read whole. A file that is
                // missing variables but looks like any other capture is worse than no
                // file: months later nobody can tell the difference.
                if (snapshot.Truncated || snapshot.FailedCount > 0)
                {
                    progress?.Report(new CaptureProgress(block, CaptureOutcome.Failed, Why(snapshot)));
                    return;
                }

                string target = ExportFile.Unique(
                    settings.Folder, ExportFile.NameFor(settings.Address, block, started));

                XlsxExporter.Write(snapshot, target);

                progress?.Report(new CaptureProgress(
                    block,
                    CaptureOutcome.Written,
                    string.Format(
                        "{0:N0} variables in {1:N1} s  ->  {2}",
                        snapshot.Rows.Count,
                        snapshot.Duration.TotalSeconds,
                        System.IO.Path.GetFileName(target))));
            }
            catch (PlcException exception)
            {
                progress?.Report(new CaptureProgress(block, CaptureOutcome.Failed, exception.Message));
            }
            catch (Exception exception)
            {
                progress?.Report(new CaptureProgress(block, CaptureOutcome.Failed, exception.Message));
            }
        }

        private static string Why(Snapshot snapshot)
        {
            if (snapshot.Truncated)
                return "Not written: the browse stopped at a limit, so variables are missing.";

            return string.Format(
                "Not written: {0:N0} of {1:N0} variables could not be read.",
                snapshot.FailedCount, snapshot.Rows.Count);
        }
    }
}
