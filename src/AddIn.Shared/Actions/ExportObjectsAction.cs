using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

using AddIn.Shared.Adapters;

using Core.Config;
using Core.Exports;

namespace AddIn.Shared.Actions
{
    /// <summary>
    /// Writes the selected objects to <c>.plc-framework\exports\</c>, in a tree shaped like
    /// the project's own.
    ///
    /// **No window.** The result is files on disk, and what the operator needs afterwards is
    /// the count and the folder - which TIA's own notification says perfectly well. A window
    /// would exist only to be closed.
    ///
    /// **Nothing is compiled, and nothing is deleted.** A block TIA refuses to export because
    /// it is not consistent is reported by name; files from an earlier export that no longer
    /// match anything in the project are left where they are, because deciding they are
    /// rubbish is not this action's call.
    /// </summary>
    public static class ExportObjectsAction
    {
        public const string Title = "Export objects";

        /// <summary>
        /// Not embedded yet: <c>Icons.Get</c> answers null and the menu falls back to an entry
        /// without one. Drop the file into <c>assets\AddIn\</c> and it appears.
        /// </summary>
        public const string IconPath = "AddIn/export.ico";

        /// <summary>How many failures a notification names before it only counts the rest.</summary>
        private const int ListedProblems = 10;

        /// <summary>
        /// How many objects pass between two updates of the text TIA shows. Cancellation is
        /// asked about on every one of them; the text is a call into the host.
        /// </summary>
        private const int ProgressEvery = 25;

        /// <param name="scope">What was selected, in words: "PLC", "2 block folders".</param>
        /// <param name="walk">Reads the selection out of the project, as objects that can export themselves.</param>
        public static void Execute(
            ITiaNotifier notifier,
            ITiaBusy busy,
            string projectDirectory,
            string scope,
            Func<IEnumerable<ExportItem>> walk)
        {
            if (notifier == null || walk == null) return;

            // Everything is written inside the project, so a project that was never saved has
            // nowhere to put it. That is a sentence, not a failure.
            string exports = ConfigPaths.ExportsFor(projectDirectory);

            if (exports == null)
            {
                notifier.Info(Title,
                    "\n\nThis project has no folder yet.\n\n" +
                    "It has not been saved, so there is nowhere to export to. Save it and try again.");
                return;
            }

            if (busy == null)
            {
                Run(notifier, projectDirectory, exports, scope, walk, null);
                return;
            }

            busy.While(Title + ": " + scope, progress =>
            {
                Run(notifier, projectDirectory, exports, scope, walk, progress);
                return null;
            });
        }

        private static void Run(
            ITiaNotifier notifier,
            string projectDirectory,
            string exports,
            string scope,
            Func<IEnumerable<ExportItem>> walk,
            Progress progress)
        {
            List<ExportItem> items;
            try
            {
                Stop(progress, "Reading the selection...");
                items = new List<ExportItem>(walk() ?? new List<ExportItem>());
            }
            catch (Exception e)
            {
                // The walk is unguarded on purpose: a part of the project that could not be
                // read fails here, by name, rather than leaving an export that looks whole.
                notifier.Error(Title, "\n\nThe selection could not be read from the project.\n\n" + e.Message);
                return;
            }

            if (items.Count == 0)
            {
                notifier.Info(Title,
                    "\n\nNothing to export in the selection.\n\n" +
                    "Select the project, a PLC, a software unit, a folder, or blocks, tag tables, " +
                    "PLC data types or text lists. Objects TIA names itself - system blocks, the " +
                    "default tag table - are not exported.");
                return;
            }

            int written = 0;
            bool cancelled = false;
            List<string> refused = new List<string>();
            HashSet<string> taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int done = 0; done < items.Count; done++)
            {
                ExportItem item = items[done];

                if (Stop(progress, done % ProgressEvery == 0 ? Doing(item, done, items.Count) : null))
                {
                    cancelled = true;
                    break;
                }

                string problem = Write(item, projectDirectory, taken);

                if (problem == null) written++;
                else refused.Add(item.Name + ": " + problem);
            }

            notifier.Info(Title, Summary(exports, scope, items.Count, written, refused, cancelled));
        }

        /// <summary>
        /// One object, to the file the tree gives it.
        ///
        /// **A second object landing on the same file is refused rather than allowed to
        /// overwrite the first.** Two names TIA keeps apart are kept apart by
        /// <see cref="ExportTree"/>, so this only happens where the project itself repeats a
        /// place - and losing one of them silently would be the worst outcome of an export
        /// somebody will read as a backup.
        /// </summary>
        private static string Write(ExportItem item, string projectDirectory, HashSet<string> taken)
        {
            string path = ExportTree.FileFor(
                projectDirectory, item.Plc, item.Unit, item.Folders, item.Name, item.Extension ?? ExportTree.SimaticMl);

            if (path == null) return "There is nowhere to export to.";

            string tooLong = ExportTree.TooLong(path);
            if (tooLong != null) return tooLong;

            if (!taken.Add(path)) return "another object in this run was already exported to " + path;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));

                // Openness refuses to export onto a file that is there, and an export is a
                // mirror: what was written last time is what this replaces.
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception exception)
            {
                return exception.Message;
            }

            return item.ExportTo(path);
        }

        private static bool Stop(Progress progress, string text) => progress != null && progress(text);

        private static string Doing(ExportItem item, int done, int total) =>
            string.Format(CultureInfo.CurrentCulture, "Exporting {0} of {1} - {2}", done + 1, total, item.Name);

        /// <summary>
        /// What TIA says at the end: how many, where, and every object that would not come
        /// out. **A failure is named**, because an export that quietly skipped four blocks is
        /// a backup with four holes in it.
        /// </summary>
        private static string Summary(
            string exports, string scope, int total, int written, List<string> refused, bool cancelled)
        {
            StringBuilder text = new StringBuilder();

            text.Append("\n\n").Append(written).Append(" of ").Append(total)
                .Append(total == 1 ? " object" : " objects").Append(" exported from ").Append(scope).Append(".\n\n")
                .Append(exports).Append('\n');

            if (cancelled)
                text.Append("\nThe export was cancelled. What had already been written is still there.\n");

            if (refused.Count > 0)
            {
                text.Append('\n').Append(refused.Count == 1 ? "One object was not exported:" : "These were not exported:").Append('\n');

                for (int i = 0; i < refused.Count && i < ListedProblems; i++)
                    text.Append("  ").Append(refused[i]).Append('\n');

                int rest = refused.Count - ListedProblems;
                if (rest > 0) text.Append("  ...and ").Append(rest).Append(" more.\n");
            }

            return text.ToString();
        }
    }
}
