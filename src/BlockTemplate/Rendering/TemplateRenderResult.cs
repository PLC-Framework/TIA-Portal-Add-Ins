using System.Collections.Generic;

namespace BlockTemplate.Rendering
{
    /// <summary>
    /// What rendering a template came to: every file it renders into, or every problem - never both,
    /// since a render with a problem in it is not one anything should be written from.
    /// </summary>
    public sealed class TemplateRenderResult
    {
        internal TemplateRenderResult(IReadOnlyList<RenderedFile> files, IReadOnlyList<TemplateProblem> problems)
        {
            Files = files ?? new RenderedFile[0];
            Problems = problems ?? new TemplateProblem[0];
        }

        /// <summary>The template's own object first, then each generated type in the manifest's order.</summary>
        public IReadOnlyList<RenderedFile> Files { get; }

        public IReadOnlyList<TemplateProblem> Problems { get; }

        public bool Ok => Problems.Count == 0;
    }
}
