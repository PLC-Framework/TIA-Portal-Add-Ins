using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Core
{
    /// <summary>
    /// Arguments written into one command line so that the program started reads back exactly
    /// the strings that were meant - whatever characters they hold.
    ///
    /// **Needed because a TIA folder takes any character** - measured on the VM on 2026-09-25,
    /// where only a name over 128 was refused. A folder called <c>say "hi"</c> or ending in a
    /// backslash is legal there, and the obvious quoting breaks on both: an inner quote closes
    /// the argument early, and a backslash before the closing quote escapes it, so the value runs
    /// into whatever follows - the trap the Openness notes record for a folder path.
    ///
    /// **The rules are the ones Windows' own parser applies**, <c>CommandLineToArgvW</c> and the
    /// .NET runtime's reading of <c>Main</c>'s arguments alike: a quote is written as <c>\"</c>,
    /// backslashes are doubled only where a quote follows them - the one inside a value and the
    /// one that closes it - and left as they are everywhere else, so a path reads naturally.
    /// </summary>
    public static class CommandLine
    {
        /// <summary>The arguments joined by spaces, each <see cref="Quote">quoted</see>.</summary>
        public static string Join(IEnumerable<string> arguments) =>
            string.Join(" ", (arguments ?? Enumerable.Empty<string>()).Select(Quote));

        /// <summary>
        /// One argument, always inside quotes. **Always, not only when it holds a space**: an
        /// empty value must still take its position, and one rule is easier to trust than a rule
        /// and its exceptions. Null is written as the empty argument.
        /// </summary>
        public static string Quote(string argument)
        {
            string value = argument ?? string.Empty;
            StringBuilder quoted = new StringBuilder(value.Length + 2);

            quoted.Append('"');

            for (int i = 0; i < value.Length; i++)
            {
                int backslashes = 0;

                while (i < value.Length && value[i] == '\\')
                {
                    backslashes++;
                    i++;
                }

                if (i == value.Length)
                {
                    // Before the closing quote: doubled, or the last one would escape it.
                    quoted.Append('\\', backslashes * 2);
                    break;
                }

                if (value[i] == '"')
                {
                    // Before a quote in the value: doubled, and one more to escape the quote.
                    quoted.Append('\\', backslashes * 2 + 1);
                    quoted.Append('"');
                    continue;
                }

                // Anywhere else a backslash is only a backslash.
                quoted.Append('\\', backslashes);
                quoted.Append(value[i]);
            }

            quoted.Append('"');

            return quoted.ToString();
        }
    }
}
