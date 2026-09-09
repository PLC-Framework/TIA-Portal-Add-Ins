namespace Core.Config.Validation
{
    /// <summary>
    /// One thing wrong with a configuration, and where it is.
    /// </summary>
    public sealed class ValidationIssue
    {
        public ValidationIssue(string path, string message)
        {
            Path = path;
            Message = message;
        }

        /// <summary>
        /// Where the problem is, in the document's own terms:
        /// <c>projectConfig.codingStyle.rules[3].regex</c>.
        ///
        /// Not a file path. This is what lets the editor put the cursor on the offending
        /// field instead of showing a message about a file, and what lets a report name
        /// twelve problems that a reader can act on one at a time.
        /// </summary>
        public string Path { get; }

        /// <summary>
        /// What is wrong, phrased so it reads on its own - and deliberately without
        /// repeating <see cref="Path"/>, which is already alongside it.
        /// </summary>
        public string Message { get; }

        public override string ToString() => Path + ": " + Message;
    }
}
