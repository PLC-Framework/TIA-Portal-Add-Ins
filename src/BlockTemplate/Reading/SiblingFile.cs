namespace BlockTemplate.Reading
{
    /// <summary>A file beside a template's manifest, of the same base and major, with what its name says.</summary>
    internal sealed class SiblingFile
    {
        public SiblingFile(string path, TemplateFileName name)
        {
            Path = path;
            Name = name;
        }

        public string Path { get; }

        public TemplateFileName Name { get; }

        /// <summary>Its name, without its folder - what a problem is said against.</summary>
        public string File => System.IO.Path.GetFileName(Path);
    }
}
