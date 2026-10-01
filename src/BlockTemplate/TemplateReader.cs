using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Core.Imports;

namespace BlockTemplate
{
    /// <summary>
    /// Reads a template - its CONFIG-JSON, the file it renders and its sub-templates - and says
    /// everything wrong with it. **Never throws**: a file that has gone, a folder that will not list
    /// and a CONFIG-JSON that does not parse are all problems in the answer.
    ///
    /// **A template is read through its <c>.json</c>**, which names it: <c>_oc_conveyor.v1.json</c>.
    /// Beside it, by name, are the file it renders - exactly one, <c>_oc_conveyor.v1.xml</c> or in
    /// any other format TIA takes in - and its sub-templates: the same base and the same major, with
    /// an id between them, <c>_oc_conveyor.settings.v1.udt</c>. Each <c>generatedTypes</c> entry
    /// needs exactly one, and each one needs its entry: a sub-template nothing describes is a type
    /// that would never be generated, and an entry with no file is one that cannot be.
    ///
    /// **Nothing here reads inside a rendered file.** The CONFIG-JSON lived in the template's first
    /// comment until TIA refused a <c>.s7dcl</c> carrying a <c>(* *)</c> (2026-10-01), and with it
    /// went the reader that found that comment and the checks it carried: what a file holds is
    /// TIA's to refuse when it is imported, as it is for any other import.
    /// </summary>
    public static class TemplateReader
    {
        /// <param name="path">The template's CONFIG-JSON: <c>&lt;base&gt;.v&lt;major&gt;.json</c>.</param>
        public static TemplateRead Read(string path)
        {
            List<TemplateProblem> problems = new List<TemplateProblem>();
            string file = SafeName(path);

            TemplateFileName name = TemplateFileName.Parse(file, out string problem);
            if (name == null) return Failed(problems, file, problem);

            if (!name.IsMain)
                return Failed(problems, file, (name.IsConfig ? "A sub-template has no .json of its own: it is " : "It is ") +
                                              "part of the template " + name.ConfigName + " - read that instead.");

            if (!name.IsConfig)
                return Failed(problems, file, "A template is read through its CONFIG-JSON: read " + name.ConfigName + " instead.");

            // A folder that will not list leaves the CONFIG-JSON still to read: it is a file that may
            // well open, and says nothing about the folder. Only the files beside it go unchecked -
            // every one of them would otherwise be reported missing. Codex's seventh review of
            // stage 1.1 found the read stopping here (2026-10-01).
            List<Sibling> siblings = Siblings(path, name, problems, file);
            bool listed = siblings != null;

            string json = ReadText(path, problems, file);

            bool typesWhole = false;
            TemplateConfig config = json == null ? null : ConfigParser.Parse(json, file, problems, out typesWhole);

            TemplateFile main = listed ? Main(siblings, name, problems, file) : null;
            List<TemplatePart> parts = listed ? Parts(siblings, config, typesWhole, name, problems, file) : null;

            return problems.Count > 0
                ? new TemplateRead(null, problems)
                : new TemplateRead(new Template(name.Base, name.Major, path, config, main, parts), problems);
        }

        /// <summary>
        /// The file the template renders - exactly one beside its <c>.json</c>, and a <c>.s7res</c>
        /// only as the second half of a <c>.s7dcl</c> - or null, every fault said.
        /// </summary>
        private static TemplateFile Main(List<Sibling> siblings, TemplateFileName name, List<TemplateProblem> problems, string file)
        {
            List<Sibling> group = siblings.Where(s => s.Name.IsMain && !s.Name.IsConfig).ToList();

            // A .s7res beside anything but a .s7dcl belongs to nothing, and left unsaid it would sit in
            // the folder looking like part of a template that reads cleanly. Codex's review found it.
            bool paired = Paired(group, problems, "template");

            List<Sibling> mains = group.Where(s => !s.Name.IsResource).ToList();

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

            Sibling chosen = mains[0];
            string text = ReadText(chosen.Path, problems, chosen.File);

            Sibling resource = group.FirstOrDefault(s => s.Name.IsResource);
            string resourceText = resource == null ? null : ReadText(resource.Path, problems, resource.File);

            if (text == null || (resource != null && resourceText == null)) return null;

            return new TemplateFile(chosen.Path, chosen.Name, ImportFiles.FormatOf(chosen.Path).Value, text,
                                    resource?.Path, resourceText);
        }

        /// <summary>
        /// Every sub-template matched to the generated type that describes it, in the CONFIG-JSON's
        /// order - and every one left over, either way, said.
        /// </summary>
        /// <param name="typesWhole">
        /// Every <c>generatedTypes</c> entry read. Only then is a sub-template no entry describes a
        /// stray: otherwise it may be the one a broken entry meant, and saying so would be a second
        /// problem about the first.
        /// </param>
        private static List<TemplatePart> Parts(List<Sibling> siblings, TemplateConfig config, bool typesWhole,
                                                TemplateFileName main, List<TemplateProblem> problems, string file)
        {
            List<TemplatePart> parts = new List<TemplatePart>();

            // A sub-template is described by the template's .json and has none of its own: one would
            // be a second description nobody reads, waiting to disagree with the first.
            foreach (Sibling stray in siblings.Where(s => !s.Name.IsMain && s.Name.IsConfig))
                problems.Add(new TemplateProblem(stray.File, null, "A sub-template has no .json of its own: " + main.ConfigName +
                                                                   " describes it in its generatedTypes."));

            Dictionary<string, List<Sibling>> byId = siblings
                .Where(s => !s.Name.IsMain && !s.Name.IsConfig)
                .GroupBy(s => s.Name.Part, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            // What a group is on its own - one format, a .s7res beside a .s7dcl - is asked of every
            // group before anything else, and whatever the CONFIG-JSON says: whether an entry
            // describes a file and whether the file is sound are two facts, and one must not wait on
            // the other. Codex's fourth, fifth and seventh reviews each found one waiting (2026-10-01).
            Dictionary<string, bool> sound = byId.ToDictionary(g => g.Key, g => Sound(g.Value, problems), StringComparer.OrdinalIgnoreCase);

            // And every sub-template is read, matched or not, so a file that will not open is said
            // whatever describes it. Codex's eleventh review found it read only when matched. A
            // .s7res is read too, since it is rendered with its .s7dcl.
            Dictionary<string, string> texts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Sibling part in siblings.Where(s => !s.Name.IsMain && !s.Name.IsConfig))
                texts[part.Path] = ReadText(part.Path, problems, part.File);

            // With no CONFIG-JSON there is nothing to hold them against, and calling every one of
            // them stray would bury the one problem that matters under several that do not.
            if (config == null) return parts;

            foreach (GeneratedType type in config.GeneratedTypes)
            {
                if (!byId.TryGetValue(type.Id, out List<Sibling> files))
                {
                    problems.Add(new TemplateProblem(file, null, "generatedTypes '" + type.Id + "' has no sub-template: " +
                                                                 "it needs " + main.Base + "." + type.Id + ".v" + main.Major +
                                                                 ".<extension> beside the template."));
                    continue;
                }

                if (!sound[type.Id]) continue;

                TemplateFile part = PartFile(files, texts);
                if (part != null) parts.Add(new TemplatePart(type, part));
            }

            foreach (string id in byId.Keys.Where(id => !config.GeneratedTypes.Any(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase))))
            {
                if (!typesWhole) continue;

                // Said once per file that would be generated: a .s7res is its .s7dcl's other half, not
                // a sub-template of its own - unless it is all there is. Codex's fifth review found a
                // pair counted as two (2026-10-01).
                List<Sibling> named = byId[id].Where(f => !f.Name.IsResource).ToList();
                if (named.Count == 0) named = byId[id];

                foreach (Sibling stray in named)
                    problems.Add(new TemplateProblem(stray.File, null, "No generatedTypes entry describes '" + id +
                                                                       "', so it would never be generated."));
            }

            return parts;
        }

        /// <summary>
        /// Whether one sub-template's files are one: a single format, and a <c>.s7res</c> only beside
        /// a <c>.s7dcl</c> - each fault said. Both are asked, so neither hides the other.
        /// </summary>
        private static bool Sound(List<Sibling> files, List<TemplateProblem> problems)
        {
            bool paired = Paired(files, problems, "sub-template");

            List<Sibling> templates = files.Where(f => !f.Name.IsResource).ToList();
            if (templates.Count > 1)
                problems.Add(new TemplateProblem(templates[0].File, null, "One sub-template in " + templates.Count + " formats - " +
                                                                          string.Join(", ", templates.Select(t => t.File)) +
                                                                          " - and only one can be generated."));

            return paired && templates.Count == 1;
        }

        /// <summary>
        /// One sound sub-template's file: one format, or a <c>.s7dcl</c> with its <c>.s7res</c> - its
        /// text already read, null when it would not.
        /// </summary>
        private static TemplateFile PartFile(List<Sibling> files, Dictionary<string, string> texts)
        {
            Sibling chosen = files.Single(f => !f.Name.IsResource);
            Sibling resource = files.FirstOrDefault(f => f.Name.IsResource);

            string text = texts[chosen.Path];
            string resourceText = resource == null ? null : texts[resource.Path];
            if (text == null || (resource != null && resourceText == null)) return null;

            return new TemplateFile(chosen.Path, chosen.Name, ImportFiles.FormatOf(chosen.Path).Value, text,
                                    resource?.Path, resourceText);
        }

        /// <summary>
        /// Every <c>.s7res</c> of one group - the template's, or one sub-template's - with no
        /// <c>.s7dcl</c> to sit beside, said on its own file. False when there was one.
        ///
        /// **Asked of every group, before anything else about it**: Codex's fourth review found an
        /// orphan hidden behind a group's other faults - two formats, or no entry describing it -
        /// and said only once those were fixed (2026-10-01).
        /// </summary>
        private static bool Paired(List<Sibling> files, List<TemplateProblem> problems, string what)
        {
            List<Sibling> templates = files.Where(f => !f.Name.IsResource).ToList();
            if (templates.Any(t => t.Name.Extension == ImportFiles.DocumentExtension)) return true;

            List<Sibling> resources = files.Where(f => f.Name.IsResource).ToList();

            foreach (Sibling resource in resources)
                problems.Add(new TemplateProblem(resource.File, null, templates.Count == 0
                    ? "A " + ImportFiles.ResourceExtension + " is the second half of a " + ImportFiles.DocumentExtension +
                      ", and this one has none."
                    : "A " + ImportFiles.ResourceExtension + " belongs beside a " + ImportFiles.DocumentExtension + ", and this " +
                      what + " is " + string.Join(", ", templates.Select(t => t.Name.Extension).Distinct()) + "."));

            return resources.Count == 0;
        }

        /// <summary>
        /// Every template file beside <paramref name="path"/> of the same base and major, the
        /// <c>.json</c> itself included. Null when the folder will not list, said.
        /// </summary>
        private static List<Sibling> Siblings(string path, TemplateFileName name, List<TemplateProblem> problems, string file)
        {
            try
            {
                string folder = Path.GetDirectoryName(Path.GetFullPath(path));

                return Directory.GetFiles(folder)
                    .Select(p => new Sibling(p, TemplateFileName.Parse(Path.GetFileName(p), out string _)))
                    .Where(s => s.Name != null &&
                                s.Name.Major == name.Major &&
                                string.Equals(s.Name.Base, name.Base, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(s => s.File, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception failed)
            {
                problems.Add(new TemplateProblem(file, null, "Its folder could not be listed: " + failed.Message));
                return null;
            }
        }

        private static string ReadText(string path, List<TemplateProblem> problems, string file)
        {
            try
            {
                return File.ReadAllText(path);
            }
            catch (Exception failed)
            {
                problems.Add(new TemplateProblem(file, null, "It could not be read: " + failed.Message));
                return null;
            }
        }

        private static string SafeName(string path)
        {
            try { return Path.GetFileName(path) ?? string.Empty; }
            catch (ArgumentException) { return path ?? string.Empty; }
        }

        private static TemplateRead Failed(List<TemplateProblem> problems, string file, string message)
        {
            problems.Add(new TemplateProblem(file, null, message));
            return new TemplateRead(null, problems);
        }

        /// <summary>A file beside the template, with what its name says.</summary>
        private sealed class Sibling
        {
            public Sibling(string path, TemplateFileName name)
            {
                Path = path;
                Name = name;
            }

            public string Path { get; }
            public TemplateFileName Name { get; }
            public string File => System.IO.Path.GetFileName(Path);
        }
    }
}
