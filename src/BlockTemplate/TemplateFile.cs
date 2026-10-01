using Core.Imports;

namespace BlockTemplate
{
    /// <summary>
    /// One file a template renders - the template's own or a sub-template - with its text. **Nothing
    /// in it is the template's description**: that is the <c>.json</c> beside it, so every byte here
    /// is meant for TIA Portal once rendered.
    /// </summary>
    public sealed class TemplateFile
    {
        public TemplateFile(string path, TemplateFileName name, ImportFormat format, string companion, string text)
        {
            Path = path;
            Name = name;
            Format = format;
            Companion = companion;
            Text = text;
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
    }
}
