using System.Collections.Generic;

namespace BlockTemplate.Reading
{
    /// <summary>
    /// What reading a template came to: the template, or every problem that stopped it.
    ///
    /// **All the problems, not the first.** A template is fixed by hand, and finding its faults one
    /// read at a time is what a validator exists to spare its author.
    /// </summary>
    public sealed class TemplateReadResult
    {
        internal TemplateReadResult(Template template, IReadOnlyList<TemplateProblem> problems)
        {
            Template = template;
            Problems = problems;
        }

        /// <summary>The template, or null when anything at all is wrong with it.</summary>
        public Template Template { get; }

        public IReadOnlyList<TemplateProblem> Problems { get; }

        public bool Ok => Template != null;
    }
}
