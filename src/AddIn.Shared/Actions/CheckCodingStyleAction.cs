using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

using AddIn.Shared.Adapters;

using Core;
using Core.Checks;
using Core.Config;
using Core.Config.Validation;

namespace AddIn.Shared.Actions
{
    /// <summary>
    /// Holds whatever was selected in the project tree against projectConfig.codingStyle, and
    /// hands the result to the report window.
    ///
    /// The version project decides what the selection contains and hands over a walk that
    /// produces it; this decides the order things happen in. **The Add-In computes and the
    /// satellite shows**: the report crosses as a JSON document on the child's standard
    /// input, so nothing is written anywhere nobody asked for a file. Nothing here touches
    /// Siemens.
    /// </summary>
    public static class CheckCodingStyleAction
    {
        public const string Title = "Check coding style";
        public const string IconPath = "AddIn/coding-style.ico";

        /// <summary>
        /// File name inside <see cref="InstallPaths.Root"/>. It is the AssemblyName of the
        /// Satellite.CodingStyleReport project.
        /// </summary>
        public const string ExecutableName = "PLC-Framework.Satellite.CodingStyleReport.exe";

        /// <summary>How many validation problems a notification names before it only counts the rest.</summary>
        private const int ListedProblems = 10;

        /// <summary>
        /// How many objects pass between two updates of the text TIA shows. Cancellation is
        /// asked about on every one of them - that is a property read - while the text is a
        /// call into the host, and a name replaced thirty times a second is not read by anyone.
        /// </summary>
        private const int ProgressEvery = 25;

        /// <param name="scope">What was selected, in words for the report's header: "PLC", "2 blocks".</param>
        /// <param name="walk">
        /// Reads the selection out of the project, given somewhere to export a block whose
        /// interface is wanted. A delegate rather than a list, so that it runs only once
        /// everything that can refuse has had its say: walking a PLC of several thousand
        /// objects, on TIA's own thread, only to report a missing window or a broken
        /// config.json would waste the engineer's time.
        /// </param>
        public static void Execute(
            ITiaNotifier notifier,
            IProcessLauncher launcher,
            ITiaBusy busy,
            string projectDirectory,
            string project,
            string scope,
            Func<ExportScratch, IEnumerable<CheckedObject>> walk)
        {
            if (notifier == null || launcher == null || walk == null) return;

            string path = InstallPaths.Tool(ExecutableName);

            if (string.IsNullOrEmpty(path))
            {
                notifier.Error(Title,
                    "\n\nThe installation folder could not be resolved.\n\n" +
                    $"Set {InstallPaths.RootOverrideVariable} or reinstall the framework.");
                return;
            }

            // First, and before the configuration: "not installed" is the expected failure,
            // it deserves a message naming the missing path, and no amount of walking helps.
            if (!File.Exists(path))
            {
                notifier.Error(Title, $"\n\n{ExecutableName} is not installed.\n\nExpected at:\n{path}");
                return;
            }

            ConfigLoadResult loaded = ConfigLoader.LoadFromProject(projectDirectory);
            if (!loaded.Succeeded)
            {
                notifier.Error(Title, loaded.Error);
                return;
            }

            CodingStyle style = loaded.Config.ProjectConfig?.CodingStyle;
            if (style == null)
            {
                notifier.Warning(Title, $"\n\n'{ConfigPaths.File}' has no 'projectConfig.codingStyle' section.");
                return;
            }

            // Only this concern's validator: a broken hierarchy is no reason to refuse a
            // naming check. The path prefix makes each issue point into the file as written.
            ValidationResult validation = CodingStyleValidator.Validate(style, "projectConfig.codingStyle");
            if (!validation.IsValid)
            {
                notifier.Error(Title, Invalid(validation));
                return;
            }

            // **The window goes up before any of the work**, and is told what is being
            // checked. A project-wide check is seconds to minutes, all of it on TIA's own
            // thread, and a window that only appears at the end is indistinguishable from one
            // that never appeared: the operator sees TIA busy, nothing on screen, and
            // concludes the Add-In failed.
            //
            // The work is handed to the launcher rather than the launcher handing back a
            // process, because an Add-In may not keep a Siemens engineering object in a field
            // - the Publisher refuses to package one that does.
            DateTime startedUtc = DateTime.UtcNow;

            // And TIA says it is busy while it works, with what it is on and a way to stop.
            // Without it the window is the only thing moving, while TIA itself sits there
            // ignoring clicks with nothing to say for itself.
            Func<string> report = () => busy == null
                ? Report(notifier, style, projectDirectory, project, scope, startedUtc, walk, null)
                : busy.While(
                    Title + ": " + (string.IsNullOrWhiteSpace(project) ? "the project" : project),
                    progress => Report(notifier, style, projectDirectory, project, scope, startedUtc, walk, progress));

            string error = launcher.Start(path, CheckingNotice.Arguments(project, scope), report);

            if (error != null)
                notifier.Error(Title, $"\n\n{ExecutableName} could not be started.\n\n{error}");
        }

        /// <summary>
        /// The whole check, with the window already open and waiting for what it returns.
        ///
        /// **Null means "no report", not "nothing happened"**: the launcher closes the input
        /// with nothing in it and the window says the check did not finish, which is the truth
        /// whenever this returns after a notification about why.
        /// </summary>
        private static string Report(
            ITiaNotifier notifier,
            CodingStyle style,
            string projectDirectory,
            string project,
            string scope,
            DateTime startedUtc,
            Func<ExportScratch, IEnumerable<CheckedObject>> walk,
            Progress progress)
        {
            // One folder for this run, and it goes away below whatever happens: a block is
            // exported into it to read its interface, and a project's code must not be left
            // lying in a temporary folder afterwards.
            ExportScratch scratch = ExportScratch.In(projectDirectory, startedUtc);

            try
            {
                List<CheckedObject> subjects;
                try
                {
                    // The walk is one call into TIA and cannot be interrupted part-way, so it
                    // says what it is doing before rather than during.
                    Stop(progress, "Reading the selection...");

                    subjects = Distinct(walk(scratch));
                }
                catch (Exception e)
                {
                    // The walk is deliberately unguarded, so that a part of the project which
                    // could not be read fails here, by name, instead of being left out of a
                    // report that would then look complete.
                    notifier.Error(Title, "\n\nThe selection could not be read from the project.\n\n" + e.Message);
                    return null;
                }

                if (subjects.Count == 0)
                {
                    notifier.Info(Title,
                        "\n\nNothing to check in the selection.\n\n" +
                        "Select the project, a PLC, a software unit, a folder, or blocks, tag tables, " +
                        "PLC data types or text lists. Objects TIA names itself - system blocks, the " +
                        "default tag table - are not checked.");

                    // A window is already open and waiting. An empty report is what says so
                    // there; closing its input with nothing would claim the check broke off.
                    return StyleReport
                        .Build(style, new List<CheckRow>(), project, projectDirectory, scope, startedUtc)
                        .ToJson();
                }

                CodingStyleChecker checker = new CodingStyleChecker(style);
                List<CheckRow> rows = new List<CheckRow>();
                int done = 0;

                // Interfaces are read here rather than during the walk: the checker asks an
                // object for its members only when the rules its name matched expect some, so
                // a block nobody configured an interface for is never exported at all.
                foreach (CheckedObject subject in subjects)
                {
                    // Asked on every object, said on every twenty-fifth: the question is a
                    // property read, the answer is a call into TIA and a name nobody can read
                    // at thirty a second.
                    bool speak = done % ProgressEvery == 0;

                    if (Stop(progress, speak ? Doing(subject, done, subjects.Count) : null))
                    {
                        // Nothing is sent. A report of the objects reached before the operator
                        // pressed Cancel would be a report with a silent hole in it, and the
                        // window says the check did not finish instead.
                        notifier.Info(Title, "\n\nThe check was cancelled. No report was produced.");
                        return null;
                    }

                    rows.AddRange(checker.Check(subject));
                    done++;
                }

                return StyleReport.Build(style, rows, project, projectDirectory, scope, startedUtc).ToJson();
            }
            catch (Exception e)
            {
                // Should not happen - the contract is public and was serialized inside a
                // restricted AppDomain - but a sandbox refusal is exactly the kind of thing
                // that only shows up inside TIA, and it deserves its own sentence.
                notifier.Error(Title, "\n\nThe report could not be prepared.\n\n" + e.Message);
                return null;
            }
            finally
            {
                scratch.Discard();
            }
        }

        /// <summary>
        /// Says where the check is and asks whether to stop. True means stop; a progress that
        /// is not there - no busy state, or a caller without one - never says so.
        /// </summary>
        private static bool Stop(Progress progress, string text) => progress != null && progress(text);

        /// <summary>What TIA shows while it works: how far along, and on what.</summary>
        private static string Doing(CheckedObject subject, int done, int total) =>
            string.Format(CultureInfo.CurrentCulture, "Checking {0} of {1} - {2}", done + 1, total, subject.Name);



        /// <summary>
        /// The same object reached twice - a folder selected together with a folder inside
        /// it - is reported once. Family, type, name and where it lives identify an object
        /// within a project, since TIA refuses two blocks or two tag tables of one name in
        /// one PLC. **The PLC and the unit are part of the key**: a folder of one name, and
        /// a block of one name inside it, exist in every PLC and in every software unit.
        /// </summary>
        private static List<CheckedObject> Distinct(IEnumerable<CheckedObject> found)
        {
            List<CheckedObject> subjects = new List<CheckedObject>();
            if (found == null) return subjects;

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (CheckedObject subject in found)
            {
                if (subject == null) continue;

                string key = subject.Family + " " + subject.Type + " " + subject.Plc + " " +
                             subject.Unit + " " + subject.Path + " " + subject.Name;
                if (seen.Add(key)) subjects.Add(subject);
            }

            return subjects;
        }

        private static string Invalid(ValidationResult validation)
        {
            StringBuilder text = new StringBuilder();

            text.Append($"\n\n'{ConfigPaths.File}' has problems in its coding style, so nothing was checked:\n\n");

            foreach (ValidationIssue issue in validation.Issues.Take(ListedProblems))
                text.Append("  ").Append(issue).Append('\n');

            int rest = validation.Issues.Count - ListedProblems;
            if (rest > 0) text.Append($"  ...and {rest} more.\n");

            text.Append("\nOpen Config. Editor to fix them.");
            return text.ToString();
        }
    }
}
