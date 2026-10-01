using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Core.Imports;

namespace BlockTemplate
{
    /// <summary>
    /// Reads a template - its file, its CONFIG-JSON and its sub-templates - and says everything
    /// wrong with it. **Never throws**: a file that has gone, a folder that will not list and a
    /// CONFIG-JSON that does not parse are all problems in the answer.
    ///
    /// **The sub-templates are found beside the template**, by name: the same base and the same
    /// major, with an id between them - <c>_oc_conveyor.settings.v1.udt</c> beside
    /// <c>_oc_conveyor.v1.xml</c>. Each <c>generatedTypes</c> entry needs exactly one, and each one
    /// needs its entry: a sub-template nothing describes is a type that would never be generated,
    /// and an entry with no file is one that cannot be.
    /// </summary>
    public static class TemplateReader
    {
        /// <param name="path">The template itself: <c>&lt;base&gt;.v&lt;major&gt;.&lt;extension&gt;</c>.</param>
        public static TemplateRead Read(string path)
        {
            List<TemplateProblem> problems = new List<TemplateProblem>();
            string file = SafeName(path);

            TemplateFileName name = TemplateFileName.Parse(file, out string problem);
            if (name == null) return Failed(problems, file, problem);

            if (!name.IsMain)
                return Failed(problems, file, "It is a sub-template of " + name.Base + ": read " +
                                              name.Base + ".v" + name.Major + " instead.");

            if (name.IsResource)
                return Failed(problems, file, "A " + ImportFiles.ResourceExtension + " is read beside its " +
                                              ImportFiles.DocumentExtension + ", never as a template of its own.");

            // A folder that will not list leaves the template itself still to read: its CONFIG-JSON is
            // in a file that may well open, and says nothing about the folder. Only the sub-templates
            // go unchecked - every one of them would otherwise be reported missing. Codex's seventh
            // review found the read stopping here (2026-10-01).
            List<Sibling> siblings = Siblings(path, name, problems, file);
            bool listed = siblings != null;
            if (!listed) siblings = new List<Sibling>();

            List<Sibling> mains = siblings.Where(s => s.Name.IsMain && !s.Name.IsResource).ToList();
            if (mains.Count > 1)
                problems.Add(new TemplateProblem(file, null, "Version " + name.Major + " of " + name.Base + " exists in " +
                                                             mains.Count + " formats - " + string.Join(", ", mains.Select(m => m.File)) +
                                                             " - and only one can be the template."));

            // A .s7res beside anything but a .s7dcl belongs to nothing, and left unsaid it would sit in
            // the folder looking like part of a template that reads cleanly. Codex's review found it.
            Paired(siblings.Where(s => s.Name.IsMain).ToList(), problems, "template");

            // From here a fault in the template's own text leaves its sub-templates still to be looked
            // at: a file that will not read, or carries no CONFIG-JSON, says nothing about the files
            // beside it. Codex's fifth review found them waiting on it (2026-10-01).
            string text = ReadText(path, problems, file);

            ConfigComment comment = null;
            if (text != null)
            {
                List<string> said = new List<string>();
                comment = ConfigComment.Find(text, name.Extension == ImportFiles.SimaticMlExtension, said);
                problems.AddRange(said.Select(s => new TemplateProblem(file, null, s)));
            }

            bool typesWhole = false;
            TemplateConfig config = comment == null
                ? null
                : ConfigParser.Parse(comment.Json, ConfigComment.LineAt(text, comment.JsonStart) - 1, file, problems, out typesWhole);

            List<TemplatePart> parts = listed ? Parts(siblings, config, typesWhole, name, problems, file) : null;

            if (problems.Count > 0) return new TemplateRead(null, problems);

            TemplateFile main = new TemplateFile(path, name, ImportFiles.FormatOf(path).Value,
                                                 Companion(siblings, name, name.Part), text, comment.Start, comment.Length);

            return new TemplateRead(new Template(name.Base, name.Major, main, config, parts), problems);
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

            Dictionary<string, List<Sibling>> byId = siblings
                .Where(s => !s.Name.IsMain)
                .GroupBy(s => s.Name.Part, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            // What a group is on its own - one format, a .s7res beside a .s7dcl - is asked of every
            // group before anything else, and whatever the CONFIG-JSON says: whether an entry
            // describes a file and whether the file is sound are two facts, and one must not wait on
            // the other. Codex's fourth, fifth and seventh reviews each found one waiting (2026-10-01).
            Dictionary<string, bool> sound = byId.ToDictionary(g => g.Key, g => Sound(g.Value, problems), StringComparer.OrdinalIgnoreCase);

            // And every sub-template is read and its comments checked, matched or not: a CONFIG-JSON
            // copied into one, or a comment left open, is wrong whatever describes the file. Codex's
            // eleventh review found it checked only for the ones an entry matched (2026-10-01).
            Dictionary<string, string> texts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Sibling part in siblings.Where(s => !s.Name.IsMain && !s.Name.IsResource))
            {
                string text = ReadText(part.Path, problems, part.File);
                texts[part.Path] = text;
                if (text == null) continue;

                foreach (string said in ConfigComment.InPart(text, part.Name.Extension == ImportFiles.SimaticMlExtension))
                    problems.Add(new TemplateProblem(part.File, null, said));
            }

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
        /// text already read and checked, null when it would not read.
        /// </summary>
        private static TemplateFile PartFile(List<Sibling> files, Dictionary<string, string> texts)
        {
            List<Sibling> resources = files.Where(f => f.Name.IsResource).ToList();
            Sibling chosen = files.Single(f => !f.Name.IsResource);

            string text = texts[chosen.Path];
            if (text == null) return null;

            return new TemplateFile(chosen.Path, chosen.Name, ImportFiles.FormatOf(chosen.Path).Value,
                                    resources.Count > 0 ? resources[0].Path : null, text, -1, 0);
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

        /// <summary>The <c>.s7res</c> beside a <c>.s7dcl</c>, or null.</summary>
        private static string Companion(List<Sibling> siblings, TemplateFileName name, string part)
        {
            if (name.Extension != ImportFiles.DocumentExtension) return null;

            Sibling resource = siblings.FirstOrDefault(s => s.Name.IsResource &&
                                                            string.Equals(s.Name.Part, part, StringComparison.OrdinalIgnoreCase));
            return resource?.Path;
        }

        /// <summary>
        /// Every template file beside <paramref name="path"/> of the same base and major, the
        /// template itself included. Null when the folder will not list, said.
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
