using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BlockTemplate.Checking.Text
{
    /// <summary>
    /// One span of a text replaced by another string - and, through <see cref="Apply"/>, a set of
    /// them made at once. **Everything outside the spans is copied as it was**: the template's own
    /// line ends, indentation, entities and empty-element forms are the author's, and a check that
    /// rewrote them would hand TIA a document nobody wrote.
    /// </summary>
    internal sealed class TextEdit
    {
        public TextEdit(int offset, int length, string replacement)
        {
            Offset = offset;
            Length = length;
            Replacement = replacement;
        }

        public int Offset { get; }

        public int Length { get; }

        public string Replacement { get; }

        /// <summary>The text with every edit made. The spans must not overlap.</summary>
        public static string Apply(string text, IEnumerable<TextEdit> edits)
        {
            StringBuilder result = new StringBuilder(text.Length);
            int copied = 0;

            foreach (TextEdit edit in edits.OrderBy(e => e.Offset))
            {
                result.Append(text, copied, edit.Offset - copied);
                result.Append(edit.Replacement);
                copied = edit.Offset + edit.Length;
            }

            result.Append(text, copied, text.Length - copied);
            return result.ToString();
        }
    }
}
