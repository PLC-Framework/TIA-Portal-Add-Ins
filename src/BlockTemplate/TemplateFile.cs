using Core.Imports;

namespace BlockTemplate
{
    /// <summary>One file of a template - the template itself or a sub-template - with its text.</summary>
    public sealed class TemplateFile
    {
        public TemplateFile(string path, TemplateFileName name, ImportFormat format, string companion,
                            string text, int configStart, int configLength)
        {
            Path = path;
            Name = name;
            Format = format;
            Companion = companion;
            Text = text;
            ConfigStart = configStart;
            ConfigLength = configLength;
        }

        public string Path { get; }

        public TemplateFileName Name { get; }

        public ImportFormat Format { get; }

        /// <summary>
        /// The <c>.s7res</c> beside a <c>.s7dcl</c>, or null. TIA reads a <c>.s7dcl</c> with or
        /// without one (measured on the VM, 2026-09-30), so its absence is not a problem.
        /// </summary>
        public string Companion { get; }

        public string Text { get; }

        /// <summary>
        /// Where the CONFIG-JSON comment starts in <see cref="Text"/>, delimiters included, or -1
        /// in a file that carries none - every sub-template. Kept so rendering can take the comment
        /// out: it describes the template, and nothing it says may reach TIA Portal.
        /// </summary>
        public int ConfigStart { get; }

        public int ConfigLength { get; }
    }
}
