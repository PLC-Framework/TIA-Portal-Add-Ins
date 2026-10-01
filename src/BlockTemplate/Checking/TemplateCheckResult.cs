using System.Collections.Generic;

namespace BlockTemplate.Checking
{
    /// <summary>
    /// What checking a render came to: every file, checked and in TIA's order, or every problem -
    /// never both, as for a render.
    /// </summary>
    public sealed class TemplateCheckResult
    {
        internal TemplateCheckResult(IReadOnlyList<CheckedFile> files, IReadOnlyList<TemplateProblem> problems)
        {
            Files = files ?? new CheckedFile[0];
            Problems = problems ?? new TemplateProblem[0];
        }

        /// <summary>The template's own object first, then each generated type in the manifest's order.</summary>
        public IReadOnlyList<CheckedFile> Files { get; }

        public IReadOnlyList<TemplateProblem> Problems { get; }

        public bool Ok => Problems.Count == 0;
    }
}
