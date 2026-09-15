using System;
using System.Collections.Generic;
using System.Text;

namespace Core.Checks
{
    /// <summary>
    /// What the Add-In says it is checking, before it has a report to send.
    ///
    /// **A check of a whole PLC takes long enough for a window that is not there to look like
    /// a failure.** The report window is therefore started first and told what is being
    /// worked on, so it can say so while the Add-In walks, exports and checks; the report
    /// itself arrives later on the same process's standard input.
    ///
    /// It travels as command-line arguments rather than as a first line of the handoff,
    /// because the handoff is one JSON document read to the end of input - and a document
    /// that grew a header would be two parsers where there is now one.
    ///
    /// **Both ends share this type for the same reason `ConfigPaths` exists**: the flag, the
    /// quoting and the parsing are one statement. Two copies of an argument convention drift
    /// the day one of them is extended, and the failure is a window that quietly says nothing.
    /// </summary>
    public sealed class CheckingNotice
    {
        /// <summary>The flag that carries it, followed by the project and what was selected.</summary>
        public const string Flag = "--checking";

        public CheckingNotice(string project, string scope)
        {
            Project = project ?? string.Empty;
            Scope = scope ?? string.Empty;
        }

        public string Project { get; }

        /// <summary>What was selected, in words: "PLC", "2 block folders".</summary>
        public string Scope { get; }

        /// <summary>The command line for one of these, quoted so a space or a quote survives.</summary>
        public static string Arguments(string project, string scope) =>
            Flag + " " + Quote(project) + " " + Quote(scope);

        /// <summary>
        /// The notice in a command line, or null when there is none - a window opened by hand,
        /// or one started by an older Add-In.
        /// </summary>
        public static CheckingNotice From(string[] arguments)
        {
            if (arguments == null) return null;

            for (int i = 0; i < arguments.Length; i++)
            {
                if (!string.Equals(arguments[i], Flag, StringComparison.OrdinalIgnoreCase)) continue;

                // Either value may be missing on a hand-typed command line; what is there is used.
                string project = i + 1 < arguments.Length ? arguments[i + 1] : null;
                string scope = i + 2 < arguments.Length ? arguments[i + 2] : null;

                return new CheckingNotice(project, scope);
            }

            return null;
        }

        /// <summary>What it says while there is nothing to show yet.</summary>
        public string Describe()
        {
            List<string> parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(Project)) parts.Add(Project);
            if (!string.IsNullOrWhiteSpace(Scope)) parts.Add(Scope);

            return parts.Count == 0
                ? "Checking the coding style of the TIA Portal project..."
                : "Checking the coding style of " + string.Join(" - ", parts.ToArray()) + "...";
        }

        /// <summary>
        /// One argument as Windows will hand it back.
        ///
        /// The rules are the awkward ones every command line has: a quote is escaped with a
        /// backslash, and the backslashes before it are doubled - otherwise a value ending in
        /// one would escape the closing quote and swallow the argument after it.
        /// </summary>
        private static string Quote(string value)
        {
            value = value ?? string.Empty;

            StringBuilder quoted = new StringBuilder("\"");
            int backslashes = 0;

            foreach (char character in value)
            {
                if (character == '\\')
                {
                    backslashes++;
                    continue;
                }

                if (character == '"')
                {
                    quoted.Append('\\', backslashes * 2 + 1).Append('"');
                    backslashes = 0;
                    continue;
                }

                quoted.Append('\\', backslashes).Append(character);
                backslashes = 0;
            }

            return quoted.Append('\\', backslashes * 2).Append('"').ToString();
        }
    }
}
