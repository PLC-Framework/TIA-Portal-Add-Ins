using System;
using System.Collections.Generic;
using System.Linq;

using BlockTemplate.Manifest;

using Core.Imports;

namespace BlockTemplate.Reading
{
    /// <summary>
    /// The sub-templates beside a manifest, held against its <c>generatedTypes</c> both ways: an
    /// entry with no file cannot be generated, and a file no entry describes never would be.
    /// </summary>
    internal static class SubTemplateMatching
    {
        /// <summary>
        /// Every sub-template matched to the generated type that describes it, in the manifest's
        /// order - and every one left over, either way, said.
        /// </summary>
        /// <param name="manifest">What the manifest said, as far as it read; null when it did not read at all.</param>
        /// <param name="typesWhole">
        /// Every <c>generatedTypes</c> entry read. Only then is a sub-template no entry describes a
        /// stray: otherwise it may be the one a broken entry meant, and saying so would be a second
        /// problem about the first.
        /// </param>
        public static List<TemplatePart> Match(List<SiblingFile> siblings, TemplateManifest manifest, bool typesWhole,
                                               TemplateFileName main, List<TemplateProblem> problems, string file)
        {
            List<TemplatePart> parts = new List<TemplatePart>();

            // A sub-template is described by the template's manifest and has none of its own: one
            // would be a second description nobody reads, waiting to disagree with the first.
            foreach (SiblingFile stray in siblings.Where(s => !s.Name.IsMain && s.Name.IsManifest))
                problems.Add(new TemplateProblem(stray.File, null, "A sub-template has no .json of its own: " + main.ManifestName +
                                                                   " describes it in its generatedTypes."));

            Dictionary<string, List<SiblingFile>> byId = siblings
                .Where(s => !s.Name.IsMain && !s.Name.IsManifest)
                .GroupBy(s => s.Name.Part, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            // What a group is on its own is asked of every group before anything else, and whatever
            // the manifest says: whether an entry describes a file and whether the file is sound are
            // two facts, and one must not wait on the other. Codex's fourth, fifth and seventh reviews
            // of stage 1.1 each found one waiting.
            Dictionary<string, bool> sound = byId.ToDictionary(g => g.Key, g => FilePairing.Sound(g.Value, problems), StringComparer.OrdinalIgnoreCase);

            // And every sub-template is read, matched or not, so a file that will not open is said
            // whatever describes it (the eleventh). A .s7res is read too: it is rendered with its .s7dcl.
            Dictionary<string, string> texts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (SiblingFile part in siblings.Where(s => !s.Name.IsMain && !s.Name.IsManifest))
                texts[part.Path] = TemplateFolder.ReadText(part.Path, problems, part.File);

            // With no manifest there is nothing to hold them against, and calling every one of them
            // stray would bury the one problem that matters under several that do not.
            if (manifest == null) return parts;

            foreach (GeneratedType type in manifest.GeneratedTypes)
            {
                if (!byId.TryGetValue(type.Id, out List<SiblingFile> files))
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

            if (!typesWhole) return parts;

            foreach (string id in byId.Keys.Where(id => !manifest.GeneratedTypes.Any(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase))))
            {
                // Said once per file that would be generated: a .s7res is its .s7dcl's other half, not
                // a sub-template of its own - unless it is all there is. Codex's fifth review of stage
                // 1.1 found a pair counted as two.
                List<SiblingFile> named = byId[id].Where(f => !f.Name.IsResource).ToList();
                if (named.Count == 0) named = byId[id];

                foreach (SiblingFile stray in named)
                    problems.Add(new TemplateProblem(stray.File, null, "No generatedTypes entry describes '" + id +
                                                                       "', so it would never be generated."));
            }

            return parts;
        }

        /// <summary>
        /// One sound sub-template's file: one format, or a <c>.s7dcl</c> with its <c>.s7res</c> - its
        /// text already read, null when it would not.
        /// </summary>
        private static TemplateFile PartFile(List<SiblingFile> files, Dictionary<string, string> texts)
        {
            SiblingFile chosen = files.Single(f => !f.Name.IsResource);
            SiblingFile resource = files.FirstOrDefault(f => f.Name.IsResource);

            string text = texts[chosen.Path];
            string resourceText = resource == null ? null : texts[resource.Path];
            if (text == null || (resource != null && resourceText == null)) return null;

            return new TemplateFile(chosen.Path, chosen.Name, ImportFiles.FormatOf(chosen.Path).Value, text,
                                    resource?.Path, resourceText);
        }
    }
}
