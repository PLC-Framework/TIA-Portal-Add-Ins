using System.Collections.Generic;

namespace BlockTemplate.Checking.Text
{
    /// <summary>
    /// Where each line of a text starts, so a line and a column can become a position in the text
    /// and a position a line. **A line ends as XML says it does** - at <c>\r\n</c>, a lone <c>\r</c>
    /// or a lone <c>\n</c> - which is how <c>XmlReader</c> counts the lines it reports, so the two
    /// agree about where an attribute is.
    /// </summary>
    internal sealed class TextLines
    {
        private readonly List<int> _starts = new List<int> { 0 };

        public TextLines(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                if (text[i] == '\r' || text[i] == '\n') _starts.Add(i + 1);
            }
        }

        /// <summary>The position of a 1-based line and column, or -1 for one the text has not got.</summary>
        public int Offset(int line, int column)
        {
            if (line < 1 || line > _starts.Count || column < 1) return -1;
            return _starts[line - 1] + column - 1;
        }

        /// <summary>The 1-based line a position is on.</summary>
        public int LineOf(int offset)
        {
            int found = _starts.BinarySearch(offset);
            return found >= 0 ? found + 1 : ~found;
        }
    }
}
