using BlockTemplate.Manifest;

namespace BlockTemplate.Reading
{
    /// <summary>A sub-template, and the generated type its manifest entry describes.</summary>
    public sealed class TemplatePart
    {
        internal TemplatePart(GeneratedType type, TemplateFile file)
        {
            Type = type;
            File = file;
        }

        public GeneratedType Type { get; }

        public TemplateFile File { get; }
    }
}
