using System;

using Core.Logging;

namespace AddIn.Shared.Adapters
{
    /// <summary>
    /// A launcher that writes what it started, and why it could not, into the click's log.
    ///
    /// **What goes on a satellite's input is counted, never copied.** It is a report of several
    /// thousand rows, or a list of addresses and blocks the satellite's own log names anyway;
    /// the length is enough to tell "something was sent" from "nothing was".
    /// </summary>
    internal sealed class LoggedLauncher : IProcessLauncher
    {
        private readonly IProcessLauncher _inner;
        private readonly Log _log;

        public LoggedLauncher(IProcessLauncher inner, Log log)
        {
            _inner = inner;
            _log = log;
        }

        public string Start(string fileName)
        {
            return Said("started " + fileName, "could not start " + fileName, _inner.Start(fileName));
        }

        public string Start(string fileName, string standardInput)
        {
            return Said(
                "started " + fileName + ", " + Characters(standardInput) + " on its input",
                "could not start " + fileName,
                _inner.Start(fileName, standardInput));
        }

        /// <summary>
        /// The long-running shape: the window is up before the work runs, so the start is said
        /// the moment the work begins - which is also the only moment this side knows the
        /// process exists - and what was handed over once it ends.
        /// </summary>
        public string Start(string fileName, string arguments, Func<string> payload)
        {
            string target = fileName + (string.IsNullOrWhiteSpace(arguments) ? string.Empty : " " + arguments);

            // Whether the work began, which is the only way this side can tell the two failures
            // apart: the launcher answers an exception in the work with its message, exactly as
            // it answers a process that never started.
            bool started = false;

            Func<string> watched = () =>
            {
                started = true;
                _log.Info("started " + target + " - its window waits while the work runs");

                string standardInput;

                try
                {
                    standardInput = payload == null ? null : payload();
                }
                catch (Exception exception)
                {
                    // The launcher turns this into its own failure sentence, which is all the
                    // caller sees; the stack is only here.
                    _log.Failed("the work " + fileName + " was waiting for", exception);
                    throw;
                }

                _log.Info(standardInput == null
                    ? "its input closed with nothing - no result to hand over"
                    : Characters(standardInput) + " handed over on its input");

                return standardInput;
            };

            string failed = _inner.Start(fileName, arguments, watched);

            if (failed != null)
                _log.Error(started
                    ? "its input closed with nothing, because the work failed - " + failed
                    : "could not start " + target + " - " + failed);

            return failed;
        }

        public string Browse(string folder)
        {
            return Said(
                "showed " + folder + " in the file browser",
                "could not show " + folder + " in the file browser",
                _inner.Browse(folder));
        }

        /// <summary>The line for a start, or for why there was none; hands the failure back unchanged.</summary>
        private string Said(string done, string refused, string failed)
        {
            if (failed == null) _log.Info(done);
            else _log.Error(refused + " - " + failed);

            return failed;
        }

        private static string Characters(string text) =>
            (text?.Length ?? 0) + (text?.Length == 1 ? " character" : " characters");
    }
}
