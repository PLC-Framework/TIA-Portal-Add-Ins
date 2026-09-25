using System;

using Core.Logging;

namespace AddIn.Shared.Adapters
{
    /// <summary>
    /// One click of a menu entry: the log it writes, and a notifier and a launcher that record
    /// into that log whatever they tell the operator or start.
    ///
    /// **One log per click, never one per Add-In.** TIA Portal does not reload an Add-In between
    /// executions - which is why the Publisher refuses a member holding an engineering object -
    /// so a log opened once would live as long as TIA does: one START for a whole day, and no
    /// way to tell where one entry's work ended and the next began. Per click, every entry has
    /// its own run in the file and its own closing line with how long it took.
    ///
    /// **The actions do not change to be recorded.** They already report every refusal through
    /// <see cref="ITiaNotifier"/> and start every satellite through <see cref="IProcessLauncher"/>;
    /// handing them the logged versions of both means what the operator was told is what the log
    /// says, word for word, rather than a second account of the same thing that could drift.
    /// </summary>
    public sealed class MenuClick
    {
        private MenuClick(Log log, ITiaNotifier notifier, IProcessLauncher launcher)
        {
            Log = log;
            Notifier = notifier;
            Launcher = launcher;
        }

        public Log Log { get; }

        /// <summary>The operator's notifier, writing each message into the log before it is shown.</summary>
        public ITiaNotifier Notifier { get; }

        /// <summary>The launcher, writing what it started - or why it could not - into the log.</summary>
        public IProcessLauncher Launcher { get; }

        /// <summary>
        /// Opens the log, says which entry was clicked and on what, runs the entry, and closes the
        /// log whatever happened.
        ///
        /// **An exception is recorded with its stack and then let through.** What TIA Portal does
        /// with an Add-In that throws is unchanged, and recording a crash is not a reason to
        /// pretend it did not happen - the rule every satellite already follows.
        /// </summary>
        /// <param name="component">The log's name - one per TIA version, since the two Add-Ins are two packages.</param>
        /// <param name="entry">The menu text, as the operator read it.</param>
        /// <param name="selection">What was selected, already described by the version project, which alone can read it.</param>
        public static void Run(
            string component,
            string entry,
            string project,
            string projectDirectory,
            string selection,
            ITiaNotifier notifier,
            IProcessLauncher launcher,
            Action<MenuClick> work)
        {
            using (Log log = Log.For(component))
            {
                if (!string.IsNullOrWhiteSpace(project)) log.About(project, projectDirectory);

                log.Info(entry + " - " + (string.IsNullOrWhiteSpace(selection) ? "nothing selected" : selection));

                try
                {
                    work(new MenuClick(log, new LoggedNotifier(notifier, log), new LoggedLauncher(launcher, log)));
                }
                catch (Exception exception)
                {
                    log.Failed(entry, exception);
                    throw;
                }
            }
        }
    }
}
