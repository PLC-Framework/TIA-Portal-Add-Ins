using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Core.Secrets
{
    /// <summary>
    /// The <c>.env</c> at <see cref="InstallPaths.EnvFile"/>: read, and written back
    /// without disturbing anything it did not change.
    ///
    /// **Nothing here throws.** A missing file, an unreadable one, a line that makes no
    /// sense: all of them mean "that secret is not available", which callers already have
    /// to handle. Taking TIA Portal down over a malformed text file would not.
    ///
    /// The format is the usual one, and the parser is deliberately forgiving because this
    /// file is edited by hand:
    ///
    ///     # a comment
    ///     GITHUB_TOKEN=ghp_xxx
    ///     export GITHUB_TOKEN=ghp_xxx      the export prefix is tolerated
    ///     QUOTED="value with spaces"       matching quotes are stripped
    /// </summary>
    public static class DotEnv
    {
        /// <summary>Everything in the user's .env, or an empty map if there is none.</summary>
        public static IReadOnlyDictionary<string, string> Read() => ReadFrom(InstallPaths.EnvFile);

        public static IReadOnlyDictionary<string, string> ReadFrom(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return Empty();

                return Parse(File.ReadAllText(path));
            }
            catch (Exception)
            {
                return Empty();
            }
        }

        /// <summary>
        /// One secret by name, or null. Falls back to the process environment, which is
        /// what lets a build server or a test run override the file without editing it.
        /// </summary>
        public static string Get(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;

            string value;
            if (Read().TryGetValue(name, out value) && !string.IsNullOrEmpty(value)) return value;

            // Denied outright under partial trust, so it must not escape.
            try { return NullIfEmpty(Environment.GetEnvironmentVariable(name)); }
            catch (Exception) { return null; }
        }

        /// <summary>A lookup for <see cref="Variables.Expand"/>.</summary>
        public static Func<string, string> Lookup => Get;

        /// <summary>
        /// Writes one secret, creating the file and its folder if needed.
        ///
        /// **Surgical, like the config editor's own writes**: the line that defines the
        /// name is replaced in place and everything else — comments, order, unrelated
        /// entries, the user's own spacing — survives untouched. A rewrite from a
        /// dictionary would silently eat the comments explaining what each secret is for.
        /// </summary>
        /// <returns>Null on success, or a sentence naming what went wrong.</returns>
        public static string Set(string name, string value)
        {
            if (string.IsNullOrWhiteSpace(name)) return "A variable name is required.";

            string path = InstallPaths.EnvFile;
            if (string.IsNullOrEmpty(path)) return "The per-user folder could not be determined.";

            try
            {
                List<string> lines = new List<string>();
                if (File.Exists(path)) lines.AddRange(File.ReadAllLines(path));

                string replacement = name + "=" + Quote(value);
                bool replaced = false;

                for (int i = 0; i < lines.Count; i++)
                {
                    if (!NameOf(lines[i], name)) continue;

                    lines[i] = replacement;
                    replaced = true;
                    break;
                }

                if (!replaced) lines.Add(replacement);

                Directory.CreateDirectory(Path.GetDirectoryName(path));

                // No BOM: this file is read by other tools too, and a byte order mark on
                // the first line turns the first key into something no parser recognises.
                File.WriteAllLines(path, lines, new UTF8Encoding(false));

                return null;
            }
            catch (Exception exception)
            {
                return "The .env could not be written: " + exception.Message;
            }
        }

        /// <summary>Parses the text of a .env. Public so it can be exercised without a file.</summary>
        public static IReadOnlyDictionary<string, string> Parse(string text)
        {
            // Ordinal, not ignore-case: environment variable names are case sensitive
            // everywhere except the Windows shell, and treating GITHUB_TOKEN and
            // github_token as one would be a surprise the day this reads a file written
            // on another system.
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(text)) return values;

            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();

                if (line.Length == 0 || line[0] == '#') continue;

                int equals = line.IndexOf('=');
                if (equals <= 0) continue;

                string name = line.Substring(0, equals).Trim();
                if (name.StartsWith("export ", StringComparison.Ordinal))
                    name = name.Substring("export ".Length).Trim();

                if (name.Length == 0) continue;

                // Last one wins, which is what a reader of the file would assume.
                values[name] = Unquote(line.Substring(equals + 1).Trim());
            }

            return values;
        }

        /// <summary>True when this line defines that name.</summary>
        private static bool NameOf(string line, string name)
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed[0] == '#') return false;

            int equals = trimmed.IndexOf('=');
            if (equals <= 0) return false;

            string declared = trimmed.Substring(0, equals).Trim();
            if (declared.StartsWith("export ", StringComparison.Ordinal))
                declared = declared.Substring("export ".Length).Trim();

            return string.Equals(declared, name, StringComparison.Ordinal);
        }

        private static string Unquote(string value)
        {
            if (value.Length < 2) return value;

            char first = value[0];
            if ((first == '"' || first == '\'') && value[value.Length - 1] == first)
                return value.Substring(1, value.Length - 2);

            return value;
        }

        /// <summary>
        /// Quotes only when the value would not survive a round trip otherwise - leading
        /// or trailing blanks, or a character the parser treats specially. A token is
        /// plain text and stays readable.
        /// </summary>
        private static string Quote(string value)
        {
            if (value == null) return string.Empty;

            bool needs = value != value.Trim() ||
                         value.IndexOf('#') >= 0 ||
                         value.IndexOf('"') >= 0 ||
                         value.IndexOf('\'') >= 0;

            return needs ? "\"" + value.Replace("\"", "\\\"") + "\"" : value;
        }

        private static string NullIfEmpty(string value) =>
            string.IsNullOrEmpty(value) ? null : value;

        private static IReadOnlyDictionary<string, string> Empty() =>
            new Dictionary<string, string>(StringComparer.Ordinal);
    }
}
