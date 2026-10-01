using System.Collections.Generic;

using BlockTemplate.Manifest;

namespace BlockTemplate.Reading
{
    /// <summary>
    /// A template that read cleanly: its manifest and what it says, the file it renders, and the
    /// sub-template of every type it generates, in the order the manifest lists them. **Only the
    /// reader makes one**, so whoever is handed a template is handed one that read cleanly.
    /// </summary>
    public sealed class Template
    {
        internal Template(string @base, int major, string manifestPath, TemplateManifest manifest, TemplateFile main,
                          IReadOnlyList<TemplatePart> parts)
        {
            Base = @base;
            Major = major;
            ManifestPath = manifestPath;
            Manifest = manifest;
            Main = main;
            Parts = parts;
        }

        public string Base { get; }

        public int Major { get; }

        /// <summary>The template's <c>.json</c> - what names it, and what it was read through.</summary>
        public string ManifestPath { get; }

        public TemplateManifest Manifest { get; }

        /// <summary>The file it renders: the object the template describes, in the format it is written in.</summary>
        public TemplateFile Main { get; }

        public IReadOnlyList<TemplatePart> Parts { get; }
    }
}
