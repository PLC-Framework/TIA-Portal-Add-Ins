using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Core.Secrets
{
    /// <summary>
    /// <c>${NAME}</c> references, and what to put in their place.
    ///
    /// Pure: it is handed a lookup and never reads a file itself. That is what lets the
    /// same code run inside TIA Portal, inside a satellite and inside a test with three
    /// values in a dictionary.
    /// </summary>
    public static class Variables
    {
        /// <summary>
        /// <c>${NAME}</c>, where NAME is an environment-variable name: a letter or
        /// underscore, then letters, digits or underscores. Deliberately strict - a
        /// pattern loose enough to match <c>${}</c> or <c>${a b}</c> would turn typos
        /// into silent substitutions.
        /// </summary>
        private static readonly Regex Reference =
            new Regex(@"\$\{([A-Za-z_][A-Za-z0-9_]*)\}", RegexOptions.CultureInvariant);

        /// <summary>True when the whole value is one reference and nothing else.</summary>
        public static bool IsReference(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;

            Match match = Reference.Match(value.Trim());
            return match.Success && match.Length == value.Trim().Length;
        }

        /// <summary>Every variable named in the text, in order, without repeats.</summary>
        public static IReadOnlyList<string> References(string text)
        {
            List<string> names = new List<string>();
            if (string.IsNullOrEmpty(text)) return names;

            foreach (Match match in Reference.Matches(text))
            {
                string name = match.Groups[1].Value;
                if (!names.Contains(name)) names.Add(name);
            }

            return names;
        }

        /// <summary>
        /// Replaces every <c>${NAME}</c> the lookup can answer.
        ///
        /// **A reference that does not resolve is left exactly as it was**, rather than
        /// replaced with nothing. An empty string would travel on and fail somewhere far
        /// away - an HTTP 401 with no explanation - whereas a literal <c>${GITHUB_TOKEN}</c>
        /// arriving where a token was expected says precisely what went wrong. Reporting it
        /// as a problem is the environmental validator's job.
        /// </summary>
        public static string Expand(string text, Func<string, string> lookup)
        {
            if (string.IsNullOrEmpty(text) || lookup == null) return text;

            return Reference.Replace(text, match =>
            {
                string value = Safe(lookup, match.Groups[1].Value);

                // Empty counts as unresolved, not as an answer. DotEnv.Get already returns
                // null for a key present with no value, and the environmental validator
                // reports the same case as missing; a lookup that answered "" would
                // otherwise erase the reference here and disagree with both.
                return string.IsNullOrEmpty(value) ? match.Value : value;
            });
        }

        /// <summary>
        /// A lookup must not be able to take the caller down: this runs inside TIA Portal,
        /// where reading an environment variable can be denied outright.
        /// </summary>
        private static string Safe(Func<string, string> lookup, string name)
        {
            try { return lookup(name); }
            catch (Exception) { return null; }
        }
    }
}
