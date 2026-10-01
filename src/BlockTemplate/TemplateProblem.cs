namespace BlockTemplate
{
    /// <summary>
    /// One thing wrong with a template, said against its file and, when there is one, the line.
    ///
    /// **The line is the file's, never the JSON's.** The CONFIG-JSON sits inside a comment some
    /// way down the file, and a line counted from the start of the JSON would send whoever opens
    /// the template to the wrong place.
    /// </summary>
    public sealed class TemplateProblem
    {
        public TemplateProblem(string file, int? line, string message)
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
