using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using BlockTemplate.Manifest;
using BlockTemplate.Manifest.Parsing;

using Core.Imports;

namespace BlockTemplate.Reading
{
    /// <summary>
    /// Reads a template - its manifest, the file it renders and its sub-templates - and says
    /// everything wrong with it. **Never throws**: a file that has gone, a folder that will not list
    /// and a manifest that does not parse are all problems in the answer.
    ///
    /// **A template is read through its manifest**, which names it: <c>_oc_conveyor.v1.json</c>.
    /// Beside it, by name, are the file it renders - exactly one, <c>_oc_conveyor.v1.xml</c> or in
    /// any other format TIA takes in - and its sub-templates: the same base and the same major, with
    /// an id between them, <c>_oc_conveyor.settings.v1.udt</c>.
    ///
    /// **Nothing here reads inside a rendered file**: what a file holds is TIA's to refuse when it
    /// is imported, as it is for any other import.
    /// </summary>
    public static class TemplateReader
    {
        /// <param name="path">The template's manifest: <c>&lt;base&gt;.v&lt;major&gt;.json</c>.</param>
        public static TemplateReadResult Read(string path)
        {
            List<TemplateProblem> problems = new List<TemplateProblem>();
            string file = SafeName(path);

            TemplateFileName name = TemplateFileName.Parse(file, out string problem);
            if (name == null) return Failed(problems, file, problem);

            if (!name.IsMain)
                return Failed(problems, file, (name.IsManifest ? "A sub-template has no .json of its own: it is " : "It is ") +
                                              "part of the template " + name.ManifestName + " - read that instead.");

            if (!name.IsManifest)
                return Failed(problems, file, "A template is read through its manifest: read " + name.ManifestName + " instead.");

            // A folder that will not list leaves the manifest still to read: it is a file that may
            // well open, and says nothing about the folder. Only the files beside it go unchecked -
            // every one of them would otherwise be reported missing. Codex's seventh review of
            // stage 1.1 found the read stopping here.
            List<SiblingFile> siblings = TemplateFolder.Siblings(path, name, problems, file);
            bool listed = siblings != null;

            string json = TemplateFolder.ReadText(path, problems, file);

            bool typesWhole = false;
            TemplateManifest manifest = json == null ? null : ManifestParser.Parse(json, file, problems, out typesWhole);

            TemplateFile main = listed ? Main(siblings, name, problems, file) : null;
            List<TemplatePart> parts = listed ? SubTemplateMatching.Match(siblings, manifest, typesWhole, name, problems, file) : null;

            return problems.Count > 0
                ? new TemplateReadResult(null, problems)
                : new TemplateReadResult(new Template(name.Base, name.Major, path, manifest, main, parts), problems);
        }

        /// <summary>
        /// The file the template renders - exactly one beside its manifest, and a <c>.s7res</c> only
        /// as the second half of a <c>.s7dcl</c> - or null, every fault said.
        /// </summary>
        private static TemplateFile Main(List<SiblingFile> siblings, TemplateFileName name, List<TemplateProblem> problems, string file)
        {
            List<SiblingFile> group = siblings.Where(s => s.Name.IsMain && !s.Name.IsManifest).ToList();

            // A .s7res beside anything but a .s7dcl belongs to nothing, and left unsaid it would sit in
            // the folder looking like part of a template that reads cleanly. Codex's review found it.
            bool paired = FilePairing.Paired(group, problems, "template");

            List<SiblingFile> mains = group.Where(s => !s.Name.IsResource).ToList();

            if (mains.Count == 0)
            {
                problems.Add(new TemplateProblem(file, null, "It has nothing to render: it needs " + name.Base + ".v" + name.Major +
                                                             ".<extension> beside it, in a format TIA Portal takes in."));
                return null;
            }

            if (mains.Count > 1)
            {
                problems.Add(new TemplateProblem(file, null, "Version " + name.Major + " of " + name.Base + " exists in " +
                                                             mains.Count + " formats - " + string.Join(", ", mains.Select(m => m.File)) +
                                                             " - and only one can be the template."));
                return null;
            }

            if (!paired) return null;

            SiblingFile chosen = mains[0];
            string text = TemplateFolder.ReadText(chosen.Path, problems, chosen.File);

            SiblingFile resource = group.FirstOrDefault(s => s.Name.IsResource);
            string resourceText = resource == null ? null : TemplateFolder.ReadText(resource.Path, problems, resource.File);

            if (text == null || (resource != null && resourceText == null)) return null;

            return new TemplateFile(chosen.Path, chosen.Name, ImportFiles.FormatOf(chosen.Path).Value, text,
                                    resource?.Path, resourceText);
        }

        private static string SafeName(string path)
        {
            try { return Path.GetFileName(path) ?? string.Empty; }
            catch (ArgumentException) { return path ?? string.Empty; }
        }

        private static TemplateReadResult Failed(List<TemplateProblem> problems, string file, string message)
        {
            problems.Add(new TemplateProblem(file, null, message));
            return new TemplateReadResult(null, problems);
        }
    }
}
