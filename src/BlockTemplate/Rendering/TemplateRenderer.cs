using System;
using System.Collections.Generic;
using System.IO;

using BlockTemplate.Manifest;
using BlockTemplate.Reading;

using Core.Imports;

namespace BlockTemplate.Rendering
{
    /// <summary>
    /// Renders a template that read cleanly, with the values a form filled in, into the text of every
    /// file it makes - or into every problem, never both. Pure: nothing is read from disk and nothing
    /// is written.
    ///
    /// **What the template sees** (stage 1.2, 2026-10-01): <c>header.&lt;field&gt;</c>, or
    /// <c>header["another-value"]</c> for a field that is not a word; every variable bare,
    /// <c>{{ sensors }}</c>; the chosen TIA version's values bare too, <c>{{ FlgNet }}</c>;
    /// <c>types.&lt;id&gt;</c>, each generated type's full name, so the name is written once, in the
    /// manifest; and <c>uid("key")</c> and <c>uid()</c>.
    ///
    /// The steps are each a type of their own: <see cref="ValuesCheck"/> holds the values against
    /// the manifest, <see cref="RenderModel"/> is what a file sees, and <see cref="ScribanText"/>
    /// is the one place Scriban runs. Every rule they follow was measured on Scriban 7.5.0 first.
    /// </summary>
    public static class TemplateRenderer
    {
        public static TemplateRenderResult Render(Template template, TemplateValues values)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            if (values == null) throw new ArgumentNullException(nameof(values));

            List<TemplateProblem> problems = new List<TemplateProblem>();
            RenderModel model = ValuesCheck.Resolve(template, values, problems);
            if (problems.Count > 0) return new TemplateRenderResult(null, problems);

            List<RenderedFile> files = new List<RenderedFile>();

            files.Add(RenderFile(template.Main, null, model.Name, model, problems));
            foreach (TemplatePart part in template.Parts)
                files.Add(RenderFile(part.File, part.Type, model.Types[part.Type.Id], model, problems));

            return problems.Count > 0 ? new TemplateRenderResult(null, problems) : new TemplateRenderResult(files, problems);
        }

        /// <summary>
        /// One file and its <c>.s7res</c>, rendered with one <c>uid()</c> between them: a SIMATIC SD
        /// pair links its texts by id, so the two are one document as far as numbering goes.
        /// </summary>
        private static RenderedFile RenderFile(TemplateFile source, GeneratedType type, string name, RenderModel model, List<TemplateProblem> problems)
        {
            UidCounter uids = new UidCounter();

            string text = ScribanText.Render(source.Text, Path.GetFileName(source.Path),
                                             model.Script(source.Format == ImportFormat.SimaticMl, uids), problems);
            string companion = source.CompanionText == null
                ? null
                : ScribanText.Render(source.CompanionText, Path.GetFileName(source.Companion), model.Script(false, uids), problems);

            if (text == null || (source.CompanionText != null && companion == null)) return null;

            return new RenderedFile(source, type, string.IsNullOrEmpty(name) ? null : name, text, companion);
        }
    }
}
