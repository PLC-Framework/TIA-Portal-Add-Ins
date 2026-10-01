namespace BlockTemplate
{
    /// <summary>
    /// One thing wrong with a template, said against its file and, when there is one, the line:
    /// the manifest's, a template file's, or the values a form filled in, held against the manifest.
    /// </summary>
    public sealed class TemplateProblem
    {
        internal TemplateProblem(string file, int? line, string message, bool rendered = false)
        {
            File = file;
            Line = line;
            Message = message;
            Rendered = rendered;
        }

        /// <summary>The file's name, without its folder.</summary>
        public string File { get; }

        /// <summary>The line in that file, 1-based, or null when the problem has none.</summary>
        public int? Line { get; }

        public string Message { get; }

        /// <summary>
        /// True when the problem was found in what the file rendered into rather than in the file
        /// as written - a check of the output, stage 1.3 - so <see cref="Line"/> counts the lines of
        /// that output. A loop turns one line of a template into many, and pointing the author at the
        /// template's line 52 for the output's line 52 would send them to the wrong place.
        /// </summary>
        public bool Rendered { get; }

        public override string ToString() =>
            File + (Line.HasValue ? (Rendered ? ", rendered line " : ", line ") + Line.Value : Rendered ? ", as rendered" : string.Empty) +
            ": " + Message;
    }
}
