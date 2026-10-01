namespace BlockTemplate
{
    /// <summary>A sub-template, and the generated type its CONFIG-JSON entry describes.</summary>
    public sealed class TemplatePart
    {
        public TemplatePart(GeneratedType type, TemplateFile file)
        {
            Type = type;
            File = file;
        }

        public GeneratedType Type { get; }

        public TemplateFile File { get; }
    }
}
