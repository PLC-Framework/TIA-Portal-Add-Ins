using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

using Core.Config;

namespace Core.Imports
{
    /// <summary>
    /// The objects a source declares - <c>.scl</c>, <c>.udt</c>, <c>.db</c>, <c>.awl</c> - and a
    /// SIMATIC SD <c>.s7dcl</c>, which declares its one object the same way:
    ///
    /// <code>
    /// FUNCTION_BLOCK "_oc_template"
    /// FUNCTION "_queue" : Int
    /// DATA_BLOCK "OC_DATA"
    /// TYPE "queueInstanceAttributes"
    /// ORGANIZATION_BLOCK "MAIN"
    /// </code>
    ///
    /// **Comments and strings are taken out before anything is matched**, so a declaration
    /// somebody commented out - with <c>//</c>, <c>(* *)</c> or <c>/* */</c> - is not read as one.
    /// The core's own sources open their documentation with <c>//(*--</c>, which is exactly the
    /// shape a line-by-line reading gets wrong.
    ///
    /// **Only names, never the body.** Nothing here parses a declaration section or a
    /// statement; that belongs to TIA, which says why a source will not generate.
    /// </summary>
    internal static class SourceDeclarations
    {
        // A keyword at the start of a line, then the name - quoted as TIA writes it, or bare
        // as a hand-written source may. FUNCTION_BLOCK comes before FUNCTION, which would
        // otherwise never be reached: an underscore is not a separator.
        private static readonly Regex Declaration = new Regex(
            @"^[ \t]*(?<keyword>FUNCTION_BLOCK|FUNCTION|ORGANIZATION_BLOCK|DATA_BLOCK|TYPE)[ \t]+(?:""(?<quoted>[^""\r\n]+)""|(?<bare>[A-Za-z_][A-Za-z0-9_]*))",
            RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>Every declaration in the text, in its order. Empty when there is none.</summary>
        internal static IReadOnlyList<DeclaredObject> Read(string text)
        {
            List<DeclaredObject> found = new List<DeclaredObject>();
            if (string.IsNullOrEmpty(text)) return found;

            foreach (Match match in Declaration.Matches(Uncommented(text)))
            {
                string name = match.Groups["quoted"].Success ? match.Groups["quoted"].Value : match.Groups["bare"].Value;
                if (name.Trim().Length == 0) continue;

                found.Add(new DeclaredObject(name.Trim(), KindOf(match.Groups["keyword"].Value)));
            }

            return found;
        }

        private static string KindOf(string keyword)
        {
            switch (keyword.ToUpperInvariant())
            {
                case "FUNCTION_BLOCK": return CodingStyleNames.FB;
                case "FUNCTION": return CodingStyleNames.FC;
                case "ORGANIZATION_BLOCK": return CodingStyleNames.OB;
                case "TYPE": return CodingStyleNames.PlcStruct;
                default: return DeclaredObject.DataBlock;
            }
        }

        /// <summary>
        /// The text with every comment and every string literal replaced by spaces, line breaks
        /// kept, so a match still starts where its line does.
        ///
        /// **A quoted name is kept**: <c>"..."</c> is an identifier in SCL, and the one thing
        /// this is looking for. <c>'...'</c> is a string, and what is inside it - a path, a
        /// comment-looking <c>//</c> - is not source.
        /// </summary>
        private static string Uncommented(string text)
        {
            StringBuilder kept = new StringBuilder(text.Length);
            int i = 0;

            while (i < text.Length)
            {
                char c = text[i];
                char next = i + 1 < text.Length ? text[i + 1] : '\0';

                if (c == '/' && next == '/')
                {
                    while (i < text.Length && text[i] != '\n') { kept.Append(Blank(text[i])); i++; }
                }
                else if ((c == '(' && next == '*') || (c == '/' && next == '*'))
                {
                    char close = c == '(' ? ')' : '/';
                    kept.Append("  ");
                    i += 2;

                    while (i < text.Length && !(text[i] == '*' && i + 1 < text.Length && text[i + 1] == close))
                    {
                        kept.Append(Blank(text[i]));
                        i++;
                    }

                    if (i < text.Length) { kept.Append("  "); i += 2; }
                }
                else if (c == '\'')
                {
                    kept.Append(' ');
                    i++;

                    while (i < text.Length && text[i] != '\'' && text[i] != '\n') { kept.Append(Blank(text[i])); i++; }

                    if (i < text.Length && text[i] == '\'') { kept.Append(' '); i++; }
                }
                else if (c == '"')
                {
                    // Copied through whole, so a // inside a name cannot open a comment.
                    kept.Append(c);
                    i++;

                    while (i < text.Length && text[i] != '"' && text[i] != '\n') { kept.Append(text[i]); i++; }

                    if (i < text.Length && text[i] == '"') { kept.Append('"'); i++; }
                }
                else
                {
                    kept.Append(c);
                    i++;
                }
            }

            return kept.ToString();
        }

        private static char Blank(char c) => c == '\n' || c == '\r' ? c : ' ';
    }
}
