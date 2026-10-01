using System;
using System.Collections.Generic;
using System.IO;

using BlockTemplate.Checking.SimaticMl;
using BlockTemplate.Rendering;

using Core.Imports;

namespace BlockTemplate.Checking
{
    /// <summary>
    /// Checks what a template rendered and puts it in TIA's order - stage 1.3 - so what reaches the
    /// import is a file TIA would have written itself. Pure, like the render: nothing is read from
    /// disk and nothing is written, and it never throws over a file - every problem in every file is
    /// said at once, at its line of what the file rendered into.
    ///
    /// - **Every format**: no <c>{{</c> left in it (<see cref="MarkerCheck"/>), and it declares what
    ///   the template promised (<see cref="DeclarationCheck"/>).
    /// - **SimaticML**: well-formed, every LAD and FBD network sound, and its IDs and UIds given back
    ///   in TIA's order (<see cref="SimaticMlCheck"/>). **What else a rendered <c>.xml</c> holds is
    ///   left exactly as written**: an SCL network's tokens, a GRAPH block's steps - stage 1.4's.
    /// - **SIMATIC SD and the sources are checked and not renumbered**: their numbering is stage 1.5's.
    /// </summary>
    public static class TemplateChecker
    {
        public static TemplateCheckResult Check(TemplateRenderResult rendered)
        {
            if (rendered == null) throw new ArgumentNullException(nameof(rendered));

            // A failed render has no files, and checking none would answer Ok - a clean bill for a
            // template that did not render.
            if (!rendered.Ok) throw new ArgumentException("A render with problems has nothing to check.", nameof(rendered));

            List<TemplateProblem> problems = new List<TemplateProblem>();
            List<CheckedFile> files = new List<CheckedFile>();

            foreach (RenderedFile file in rendered.Files)
                files.Add(CheckFile(file, problems));

            return problems.Count > 0 ? new TemplateCheckResult(null, problems) : new TemplateCheckResult(files, problems);
        }

        private static CheckedFile CheckFile(RenderedFile rendered, List<TemplateProblem> problems)
        {
            int before = problems.Count;
            string name = Path.GetFileName(rendered.Source.Path);
            ImportFormat format = rendered.Source.Format;

            MarkerCheck.Check(rendered.Text, name, problems);
            if (rendered.CompanionText != null)
                MarkerCheck.Check(rendered.CompanionText, Path.GetFileName(rendered.Source.Companion), problems);

            string text = rendered.Text;
            if (format == ImportFormat.SimaticMl)
            {
                text = SimaticMlCheck.Check(text, name, problems);
                if (text == null) return null;
            }

            IReadOnlyList<DeclaredObject> declared = DeclarationCheck.Check(text, format, rendered.Type, rendered.Name, name, problems);

            return problems.Count > before
                ? null
                : new CheckedFile(rendered.Source, rendered.Type, rendered.Name, text, rendered.CompanionText, declared);
        }
    }
}
