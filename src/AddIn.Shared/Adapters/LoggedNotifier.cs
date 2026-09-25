using Core.Logging;

namespace AddIn.Shared.Adapters
{
    /// <summary>
    /// A notifier that writes every message into the click's log before showing it.
    ///
    /// **Written first, shown second**: a message box TIA cannot show - V21's service answers
    /// null in a session without a user interface - must not also be a message nobody can find
    /// afterwards. The level follows what the operator was shown, so an error box is an ERROR
    /// line whether or not anything threw.
    /// </summary>
    internal sealed class LoggedNotifier : ITiaNotifier
    {
        private readonly ITiaNotifier _inner;
        private readonly Log _log;

        public LoggedNotifier(ITiaNotifier inner, Log log)
        {
            _inner = inner;
            _log = log;
        }

        public void Success(string caption, string message)
        {
            _log.Info(Said(caption, message));
            _inner?.Success(caption, message);
        }

        public void Info(string caption, string message)
        {
            _log.Info(Said(caption, message));
            _inner?.Info(caption, message);
        }

        public void Warning(string caption, string message)
        {
            _log.Warn(Said(caption, message));
            _inner?.Warning(caption, message);
        }

        public void Error(string caption, string message)
        {
            _log.Error(Said(caption, message));
            _inner?.Error(caption, message);
        }

        /// <summary>
        /// Trimmed before it is joined to the caption: the actions open a message with blank lines
        /// to space it under TIA's heading, and flattened as it stands that reads "About us - |".
        /// </summary>
        private static string Said(string caption, string message) =>
            "told the operator: " + (string.IsNullOrWhiteSpace(caption) ? string.Empty : caption + " - ") +
            (message ?? string.Empty).Trim();
    }
}
