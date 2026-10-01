using System.Collections.Generic;

namespace BlockTemplate
{
    /// <summary>
    /// A template that read cleanly: its CONFIG-JSON and what it says, the file it renders, and the
    /// sub-template of every type it generates, in the order the CONFIG-JSON lists them.
    /// </summary>
    public sealed class Template
    {
        public Template(string @base, int major, string configPath, TemplateConfig config, TemplateFile main,
                        IReadOnlyList<TemplatePart> parts)
        {
            Base = @base;
            Major = major;
            ConfigPath = configPath;
            Config = config;
            Main = main;
            Parts = parts;
        }

        public string Base { get; }

        public int Major { get; }

        /// <summary>The template's <c>.json</c> - what names it, and what it was read through.</summary>
        public string ConfigPath { get; }

        public TemplateConfig Config { get; }

        /// <summary>The file it renders: the object the template describes, in the format it is written in.</summary>
        public TemplateFile Main { get; }

        public IReadOnlyList<TemplatePart> Parts { get; }
    }
}
