using System;
using System.Text.RegularExpressions;

namespace Core.Logging
{
    /// <summary>
    /// A line on its way to disk, with anything that looks like a secret taken out of it.
    ///
    /// **`.plc-framework\` is under version control**, and `logs\` is only kept out of a commit
    /// by the allow-list `.gitignore` the config editor writes - which a project nobody opened
    /// the editor on has never had. So a token reaching a line here is a token one `git add`
    /// from a history, and this is the cheap half of not having that conversation.
    ///
    /// **It masks by shape rather than by knowing the secret**, because the thing that leaks is
    /// never the one somebody remembered to register: a GitHub or GitLab token, an
    /// `Authorization` header, or whatever sits after the word `password` in a message somebody
    /// wrote in a hurry.
    ///
    /// **A `${VARIABLE}` reference is left exactly as it is.** It is not a secret - it is the
    /// framework's own way of saying where the secret is *not* - and masking it would throw away
    /// the one thing the line is worth reading for when a token fails to resolve.
    ///
    /// **No `RegexOptions.Compiled`**: it emits IL, and this runs inside TIA Portal's sandbox.
    /// The patterns carry a match timeout for the same reason the coding-style checker's do.
    /// </summary>
    public static class Redacted
    {
        private const string Mask = "***";

        private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(200);

        /// <summary>A token that names its own host, which is what makes these worth matching.</summary>
        private static readonly Regex Tokens = new Regex(
            @"\b(github_pat_[A-Za-z0-9_]+|gh[pousr]_[A-Za-z0-9]+|glpat-[A-Za-z0-9\-_]+)",
            RegexOptions.None, Timeout);

        /// <summary>
        /// An `Authorization` header's own shape. **`token` is deliberately not one of these**:
        /// the separated form would turn "token expired" into "token ***" and cost exactly the
        /// sentence somebody is reading the log for. A `token=` belongs to <see cref="Named"/>.
        /// </summary>
        private static readonly Regex Bearer = new Regex(
            @"\b(Bearer|Basic)\s+(?!\$\{)[A-Za-z0-9\-._~+/=]+",
            RegexOptions.IgnoreCase, Timeout);

        /// <summary>
        /// A value introduced by a word that says what it is. The `${` guard is what keeps a
        /// `token=${REPO_TOKEN}` readable.
        /// </summary>
        private static readonly Regex Named = new Regex(
            @"\b(password|passwd|pwd|token|secret|api[_-]?key)\b(\s*[:=]\s*)(?!\$\{)(""[^""]*""|'[^']*'|[^\s,;)]+)",
            RegexOptions.IgnoreCase, Timeout);

        /// <summary>
        /// <paramref name="text"/> with every secret-shaped run replaced. Null and blank come
        /// back as they went in, and **a pattern that runs long gives the text back unmasked
        /// rather than throwing**: this is called on the way to writing a line, where an
        /// exception would cost the line and the run that was trying to explain itself.
        /// </summary>
        public static string Of(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            try
            {
                text = Tokens.Replace(text, Mask);
                text = Bearer.Replace(text, m => m.Groups[1].Value + " " + Mask);
                text = Named.Replace(text, m => m.Groups[1].Value + m.Groups[2].Value + Mask);

                return text;
            }
            catch (RegexMatchTimeoutException)
            {
                return text;
            }
        }
    }
}
