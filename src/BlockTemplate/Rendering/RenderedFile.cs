using BlockTemplate.Manifest;
using BlockTemplate.Reading;

namespace BlockTemplate.Rendering
{
    /// <summary>
    /// One file a template rendered into: the template's own, or a generated type's - with its
    /// <c>.s7res</c>, when it is a SIMATIC SD pair. Text only: where it is written is for whoever
    /// imports it.
    /// </summary>
    public sealed class RenderedFile
    {
        internal RenderedFile(TemplateFile source, GeneratedType type, string name, string text, string companionText)
        {
            Source = source;
            Type = type;
            Name = name;
            Text = text;
            CompanionText = companionText;
        }

        /// <summary>The template file it came from - its format, and its name.</summary>
        public TemplateFile Source { get; }

        /// <summary>The generated type it is, or null for the template's own object.</summary>
        public GeneratedType Type { get; }

        /// <summary>
        /// The name it was rendered under: <c>header.name</c> for the template's own object, the
        /// generated type's full name for a type. Null when the header has no <c>name</c>.
        /// </summary>
        public string Name { get; }

        public string Text { get; }

        /// <summary>The rendered <c>.s7res</c>, or null when it has none.</summary>
        public string CompanionText { get; }
    }
}
