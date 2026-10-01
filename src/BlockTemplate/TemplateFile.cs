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
        public TemplateFile(string path, TemplateFileName name, ImportFormat format, string text,
                            string companion, string companionText)
        {
            Path = path;
            Name = name;
            Format = format;
            Text = text;
            Companion = companion;
            CompanionText = companionText;
        }

        public string Path { get; }

        public TemplateFileName Name { get; }

        public ImportFormat Format { get; }

        public string Text { get; }

        /// <summary>
        /// The <c>.s7res</c> beside a <c>.s7dcl</c>, or null. TIA reads a <c>.s7dcl</c> with or
        /// without one (measured on the VM, 2026-09-30), so its absence is not a problem.
        /// </summary>
        public string Companion { get; }

        /// <summary>
        /// The <c>.s7res</c>'s text, rendered with the <c>.s7dcl</c> it belongs to: a SIMATIC SD pair
        /// links its texts by id, so the two are one document as far as <c>uid()</c> is concerned.
        /// </summary>
        public string CompanionText { get; }
    }
}
