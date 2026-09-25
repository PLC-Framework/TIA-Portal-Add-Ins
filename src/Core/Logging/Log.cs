using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Core.Logging
{
    /// <summary>
    /// What a run records about itself.
    ///
    /// **One per application, appended to** (2026-09-24, the maintainer's design), in
    /// <c>%LOCALAPPDATA%\PLC-Framework\logs\</c>. Which project a line belongs to is a column
    /// on the line rather than part of the file name - see <see cref="LogPaths"/> for why, and
    /// <see cref="About"/> for when it gets filled in.
    ///
    /// **A run is found by its own identifier, and that identifier is a `Guid`** because the
    /// obvious one is not available: `Process.GetCurrentProcess()` is refused under partial
    /// trust - measured again on 2026-09-24, and refused even with everything `Config.xml`
    /// asks for. Two windows of the same satellite therefore interleave in one file and are
    /// still separable, which is the whole reason the column is there.
    ///
    /// **Nothing here throws and nothing here is required to work.** A folder that cannot be
    /// created, a per-user path the sandbox will not resolve, a full disk: each gives back a
    /// log that quietly writes nothing, because an application that fails over its own logging
    /// is worse than one that keeps no log. What was lost is counted and said in the last line.
    ///
    /// **The stamp is local with its offset** (the maintainer's choice), which the tightest
    /// partial trust allows - `DateTimeOffset.Now` and `TimeZoneInfo.Local` were both measured
    /// inside an `AppDomain` granted `Execution` alone. It is read beside TIA Portal's own
    /// messages, where UTC would be one subtraction away from every comparison.
    /// </summary>
    public sealed class Log : IDisposable
    {
        /// <summary>How often the size is asked for again while a run is writing.</summary>
        private const long CheckEvery = 64 * 1024;

        /// <summary>
        /// The project column's width.
        ///
        /// **Padded, never truncated.** Two projects whose names share their first sixteen
        /// characters must not read as the same one, so a long name pushes its own line out of
        /// line rather than being cut back into the column. Most names are far shorter, and the
        /// column is what makes "everything that happened in Plant1" one filter.
        /// </summary>
        private const int Column = 16;

        /// <summary>What the column says before a run knows which project it is working on.</summary>
        private const string Unknown = "-";

        private readonly object _gate = new object();
        private readonly string _path;
        private readonly string _previous;
        private readonly string _run;
        private readonly DateTimeOffset _opened;

        private string _project = Unknown;
        private long _sinceChecked;
        private int _written;
        private int _dropped;
        private bool _closed;

        private Log(string path, string run)
        {
            _path = path;
            _previous = LogPaths.PreviousOf(path);
            _run = run;
            _opened = Now();
        }

        /// <summary>How many lines could not be written, which the closing line reports.</summary>
        public int Dropped
        {
            get { lock (_gate) return _dropped; }
        }

        /// <summary>Whether anything is actually being written, for a caller that wants to say so.</summary>
        public bool IsWriting
        {
            get { return _path != null; }
        }

        /// <summary>
        /// One application's log, opened and headed with a line naming the run.
        ///
        /// **Opened at startup, before anything is known.** That is the point of the per-user
        /// folder: the resolver that finds Openness, the attach that picks a TIA Portal and
        /// every way either can fail all happen before there is a project, and all of them are
        /// what somebody sending a log needs it to contain.
        ///
        /// **Never null, whatever went wrong**, and the folder is created here by the first
        /// thing that writes into it.
        /// </summary>
        public static Log For(string component)
        {
            string path = LogPaths.FileFor(component);

            if (path == null) return Nothing();

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
            }
            catch (Exception)
            {
                return Nothing();
            }

            LogFile.Roll(path, LogPaths.PreviousOf(path), LogPaths.Limit);

            Log log = new Log(path, Guid.NewGuid().ToString("N").Substring(0, 8));

            log.Write(LogLevel.Start, log.Opening(component));

            return log;
        }

        /// <summary>A log that writes nothing, for everything that has nowhere to write.</summary>
        public static Log Nothing()
        {
            return new Log(null, null);
        }

        /// <summary>
        /// Which project this run turned out to be working on: named once, on a line of its
        /// own carrying the full path, and carried in the column of every line after it.
        ///
        /// **The path goes on that one line and never in the column.** It is the same string
        /// for the whole run and far too long to repeat, where the name is what somebody
        /// filters on. Two projects of one name are then still told apart by their paths,
        /// which is the only thing the file name used to do.
        /// </summary>
        public void About(string project, string path)
        {
            string named = string.IsNullOrWhiteSpace(project) ? Unknown : Flat(project).Trim();

            lock (_gate)
            {
                _project = named;
            }

            Write(LogLevel.Info, string.IsNullOrWhiteSpace(path)
                ? "working on " + named
                : "working on " + named + " - " + Flat(path));
        }

        public void Info(string message)
        {
            Write(LogLevel.Info, Flat(message));
        }

        public void Warn(string message)
        {
            Write(LogLevel.Warn, Flat(message));
        }

        public void Error(string message)
        {
            Write(LogLevel.Error, Flat(message));
        }

        /// <summary>
        /// An exception, with what was being attempted when it arrived.
        ///
        /// **The stack goes in the same write as the message**, indented under it. One call
        /// takes the file exclusively for the whole block, so no other process can land in the
        /// middle of a stack trace - which is the one thing multi-line output would otherwise
        /// lose in a file two windows share.
        /// </summary>
        public void Failed(string what, Exception failed)
        {
            if (failed == null)
            {
                Error(what);
                return;
            }

            StringBuilder said = new StringBuilder();

            said.Append(Flat(what)).Append(" - ").Append(failed.GetType().Name)
                .Append(": ").Append(Flat(failed.Message));

            for (Exception inner = failed.InnerException; inner != null; inner = inner.InnerException)
                said.Append(" <- ").Append(inner.GetType().Name).Append(": ").Append(Flat(inner.Message));

            string stack = failed.StackTrace;

            if (!string.IsNullOrEmpty(stack))
                foreach (string frame in stack.Split('\n'))
                {
                    string one = frame.Trim();

                    if (one.Length > 0) said.Append(Environment.NewLine).Append("        ").Append(one);
                }

            Write(LogLevel.Error, said.ToString());
        }

        /// <summary>
        /// The closing line: how long the run took, how much it said, and what it could not
        /// write. **Writing twice is not a failure** - a satellite that disposes on the way out
        /// of a window it already closed is the ordinary case.
        /// </summary>
        public void Dispose()
        {
            lock (_gate)
            {
                if (_closed) return;

                _closed = true;
            }

            // The line counts itself, so the number matches what somebody counts in the file
            // rather than being one short of it for a reason only this method knows.
            Write(LogLevel.End, string.Format(
                CultureInfo.InvariantCulture,
                "{0} lines, {1} dropped, {2:0.0} s",
                _written + 1, _dropped, (Now() - _opened).TotalSeconds), true);
        }

        private void Write(LogLevel level, string message)
        {
            Write(level, message, false);
        }

        private void Write(LogLevel level, string message, bool closing)
        {
            if (_path == null) return;

            lock (_gate)
            {
                if (_closed && !closing) return;

                // Flattened by the caller rather than here, so that the one thing that is
                // deliberately several lines - an exception's stack under its own message -
                // survives. It is still a single write, so nothing can interleave inside it.
                string line = Stamp() + "  " + _run + "  " + _project.PadRight(Column) + "  " +
                              level.ToString().ToUpperInvariant().PadRight(5) + "  " +
                              Redacted.Of(message) + Environment.NewLine;

                if (LogFile.Append(_path, line))
                {
                    _written++;
                    _sinceChecked += line.Length;

                    if (_sinceChecked >= CheckEvery)
                    {
                        _sinceChecked = 0;
                        LogFile.Roll(_path, _previous, LogPaths.Limit);
                    }
                }
                else
                {
                    _dropped++;
                }
            }
        }

        private string Opening(string component)
        {
            string who = Who();

            return component + " " + Product.Version + (who == null ? string.Empty : "  " + who);
        }

        /// <summary>
        /// Who is running this, when the sandbox allows it to be asked.
        ///
        /// **`EnvironmentPermission` is refused at the tightest partial trust and granted by
        /// what `Config.xml` declares** - both measured - so this is the one field that may be
        /// there in a satellite's log and missing from the Add-In's. Asked once, at the start,
        /// and never again.
        /// </summary>
        private static string Who()
        {
            try
            {
                return Environment.UserName + "@" + Environment.MachineName;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static DateTimeOffset Now()
        {
            try
            {
                return DateTimeOffset.Now;
            }
            catch (Exception)
            {
                return DateTimeOffset.UtcNow;
            }
        }

        private static string Stamp()
        {
            return Now().ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// One event is one line, so a message carrying its own newlines is flattened rather
        /// than allowed to read as several events - which is what it would become the moment
        /// another window appended between them.
        /// </summary>
        private static string Flat(string message)
        {
            if (string.IsNullOrEmpty(message)) return string.Empty;

            // Blank lines dropped rather than kept as empty separators: a paragraph break in a
            // message shown on screen - the resolver's account of where it looked is the first
            // one that had them - would otherwise read "||" in the one line meant to explain it.
            string[] lines = message.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            StringBuilder flat = new StringBuilder();

            foreach (string line in lines)
            {
                string one = line.Trim();

                if (one.Length == 0) continue;
                if (flat.Length > 0) flat.Append(" | ");

                flat.Append(one);
            }

            return flat.ToString();
        }
    }
}
