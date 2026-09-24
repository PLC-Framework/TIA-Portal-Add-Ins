using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Core.Logging
{
    /// <summary>
    /// What a run records about itself, in <c>.plc-framework\logs\</c>.
    ///
    /// **One per component, appended to** (2026-09-24, the maintainer's decision), so a file
    /// answers "what has this satellite been doing in this project" across runs rather than
    /// filling the folder with one file per launch.
    ///
    /// **A run is found by its own identifier, and that identifier is a `Guid`** because the
    /// obvious one is not available: `Process.GetCurrentProcess()` is refused under partial
    /// trust - measured again on 2026-09-24, and refused even with everything `Config.xml`
    /// asks for. Two windows of the same satellite therefore interleave in one file and are
    /// still separable, which is the whole reason the column is there.
    ///
    /// **Nothing here throws and nothing here is required to work.** A project that was never
    /// saved, a folder that cannot be created, a log denied by the sandbox: each gives back a
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

        private readonly object _gate = new object();
        private readonly string _path;
        private readonly string _previous;
        private readonly string _run;
        private readonly DateTimeOffset _opened;

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
        /// The log of one component inside one TIA project, opened and headed with a line
        /// naming the run.
        ///
        /// **Never null, whatever went wrong.** A null or blank project directory - a project
        /// never saved, a satellite with no project of its own - gives back the one that writes
        /// nothing, which is decision D of 2026-09-24 rather than a failure to report.
        ///
        /// **The folder is created here**, by the first thing that writes into it, which is the
        /// rule the rest of <c>.plc-framework\</c> already follows: a project that never ran an
        /// action collects no empty folders.
        /// </summary>
        public static Log For(string projectDirectory, string component)
        {
            string path = LogPaths.FileFor(projectDirectory, component);

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
                string line = Stamp() + "  " + _run + "  " +
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

            return message.Replace("\r\n", " | ").Replace('\n', '|').Replace('\r', '|').Trim();
        }
    }
}
