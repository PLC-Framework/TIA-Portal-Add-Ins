using System.Collections.Generic;
using System.Linq;

using Core.Imports;

namespace BlockTemplate.Reading
{
    /// <summary>
    /// What a group of files must be to be one rendered file: a single format, and a <c>.s7res</c>
    /// only as the second half of a <c>.s7dcl</c>. Asked of the template's own group and of every
    /// sub-template's.
    /// </summary>
    internal static class FilePairing
    {
        /// <summary>
        /// Whether one sub-template's files are one: a single format, and a <c>.s7res</c> only beside
        /// a <c>.s7dcl</c> - each fault said. Both are asked, so neither hides the other.
        /// </summary>
        public static bool Sound(List<SiblingFile> files, List<TemplateProblem> problems)
        {
            bool paired = Paired(files, problems, "sub-template");

            List<SiblingFile> templates = files.Where(f => !f.Name.IsResource).ToList();
            if (templates.Count > 1)
                problems.Add(new TemplateProblem(templates[0].File, null, "One sub-template in " + templates.Count + " formats - " +
                                                                          string.Join(", ", templates.Select(t => t.File)) +
                                                                          " - and only one can be generated."));

            return paired && templates.Count == 1;
        }

        /// <summary>
        /// Every <c>.s7res</c> of one group - the template's, or one sub-template's - with no
        /// <c>.s7dcl</c> to sit beside, said on its own file. False when there was one.
        ///
        /// **Asked of every group, before anything else about it**: Codex's fourth review of stage
        /// 1.1 found an orphan hidden behind a group's other faults - two formats, or no entry
        /// describing it - and said only once those were fixed.
        /// </summary>
        public static bool Paired(List<SiblingFile> files, List<TemplateProblem> problems, string what)
        {
            List<SiblingFile> templates = files.Where(f => !f.Name.IsResource).ToList();
            if (templates.Any(t => t.Name.Extension == ImportFiles.DocumentExtension)) return true;

            List<SiblingFile> resources = files.Where(f => f.Name.IsResource).ToList();

            foreach (SiblingFile resource in resources)
                problems.Add(new TemplateProblem(resource.File, null, templates.Count == 0
                    ? "A " + ImportFiles.ResourceExtension + " is the second half of a " + ImportFiles.DocumentExtension +
                      ", and this one has none."
                    : "A " + ImportFiles.ResourceExtension + " belongs beside a " + ImportFiles.DocumentExtension + ", and this " +
                      what + " is " + string.Join(", ", templates.Select(t => t.Name.Extension).Distinct()) + "."));

            return resources.Count == 0;
        }
    }
}
