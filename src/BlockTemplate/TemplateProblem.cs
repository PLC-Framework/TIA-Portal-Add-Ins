namespace BlockTemplate
{
    /// <summary>
    /// One thing wrong with a template, said against its file and, when there is one, the line:
    /// the manifest's, a template file's, or the values a form filled in, held against the manifest.
    /// </summary>
    public sealed class TemplateProblem
    {
        internal TemplateProblem(string file, int? line, string message)
        {
            File = file;
            Line = line;
            Message = message;
        }

        /// <summary>The file's name, without its folder.</summary>
        public string File { get; }

        /// <summary>The line in that file, 1-based, or null when the problem has none.</summary>
        public int? Line { get; }

        public string Message { get; }

        public override string ToString() =>
            File + (Line.HasValue ? ", line " + Line.Value : string.Empty) + ": " + Message;
    }
}
