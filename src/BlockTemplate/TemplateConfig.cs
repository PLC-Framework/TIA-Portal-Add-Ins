using System.Collections.Generic;

namespace BlockTemplate
{
    /// <summary>
    /// What a template's CONFIG-JSON says, once it has read cleanly. Every list is empty rather
    /// than null when the file leaves its key out - absent and empty mean the same here.
    /// </summary>
    public sealed class TemplateConfig
    {
        public TemplateConfig(
            IReadOnlyList<string> header, IReadOnlyList<TiaVersion> tiaVersions,
            IReadOnlyList<TemplateVariable> variables, IReadOnlyList<GeneratedType> generatedTypes)
        {
            Header = header;
            TiaVersions = tiaVersions;
            Variables = variables;
            GeneratedTypes = generatedTypes;
        }

        /// <summary>
        /// The header's field names, in the order the form shows them. **An open list, not a set
        /// of known fields** (the maintainer's design): what a future TIA version adds to a block's
        /// header is a line in a template, not a change here. Each reaches the template as
        /// <c>header.&lt;name&gt;</c>, or <c>header["another-value"]</c> when the name is not a word.
        /// </summary>
        public IReadOnlyList<string> Header { get; }

        public IReadOnlyList<TiaVersion> TiaVersions { get; }

        public IReadOnlyList<TemplateVariable> Variables { get; }

        public IReadOnlyList<GeneratedType> GeneratedTypes { get; }
    }
}
