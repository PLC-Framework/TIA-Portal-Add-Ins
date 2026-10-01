using System.Collections.Generic;

namespace BlockTemplate
{
    /// <summary>
    /// A template that read cleanly: its file, what its CONFIG-JSON says, and the sub-template of
    /// every type it generates, in the order the CONFIG-JSON lists them.
    /// </summary>
    public sealed class Template
    {
        public Template(string @base, int major, TemplateFile main, TemplateConfig config, IReadOnlyList<TemplatePart> parts)
        {
            Base = @base;
            Major = major;
            Main = main;
            Config = config;
            Parts = parts;
        }

        public string Base { get; }

        public int Major { get; }

        public TemplateFile Main { get; }

        public TemplateConfig Config { get; }

        public IReadOnlyList<TemplatePart> Parts { get; }
    }
}
