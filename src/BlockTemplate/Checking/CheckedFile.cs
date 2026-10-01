using System.Collections.Generic;

using BlockTemplate.Manifest;
using BlockTemplate.Reading;

using Core.Imports;

namespace BlockTemplate.Checking
{
    /// <summary>
    /// One file a template made, checked and put in TIA's order - **what is imported**, and the only
    /// thing that should be: a type of its own, so nothing can hand the import a rendered file that
    /// skipped the check. Text only, like what it came from: where it is written is for the import.
    /// </summary>
    public sealed class CheckedFile
    {
        internal CheckedFile(TemplateFile source, GeneratedType type, string name, string text, string companionText,
                             IReadOnlyList<DeclaredObject> declared)
        {
            Source = source;
            Type = type;
            Name = name;
            Text = text;
            CompanionText = companionText;
            Declared = declared;
        }

        /// <summary>The template file it came from - its format, and its name.</summary>
        public TemplateFile Source { get; }

        /// <summary>The generated type it is, or null for the template's own object.</summary>
        public GeneratedType Type { get; }

        /// <summary>
        /// The name it was rendered under: <c>header.name</c> for the template's own object, the
        /// generated type's full name for a type. Null when the header has no <c>name</c>.
        /// </summary>
        public string Name { get; }

        public string Text { get; }

        /// <summary>The <c>.s7res</c>, or null when it has none.</summary>
        public string CompanionText { get; }

        /// <summary>What it declares, read as the import will read it - never empty.</summary>
        public IReadOnlyList<DeclaredObject> Declared { get; }
    }
}
