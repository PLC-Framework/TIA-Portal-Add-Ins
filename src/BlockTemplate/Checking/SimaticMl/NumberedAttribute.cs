using System.Globalization;

namespace BlockTemplate.Checking.SimaticMl
{
    /// <summary>
    /// One <c>UId</c> or <c>ID</c> in a rendered document: the element that carries it, the value
    /// it has, the line it is on, and where its value sits in the text - the span a new number is
    /// written into, leaving every other character as it was.
    /// </summary>
    internal sealed class NumberedAttribute
    {
        public NumberedAttribute(string element, string value, int line, int offset, int length)
        {
            Element = element;
            Value = value;
            Line = line;
            Offset = offset;
            Length = length;
        }

        /// <summary>The local name of the element carrying it - <c>Access</c>, <c>Wire</c>, <c>NameCon</c>.</summary>
        public string Element { get; }

        public string Value { get; }

        /// <summary>The line of the rendered text it is on, 1-based.</summary>
        public int Line { get; }

        /// <summary>Where its value starts in the text, inside the quotes.</summary>
        public int Offset { get; }

        public int Length { get; }

        /// <summary>
        /// The value as a UId, or null: digits alone - nothing TIA writes has a sign or a blank - and
        /// a number an <c>int</c> holds.
        /// </summary>
        public int? Number =>
            int.TryParse(Value, NumberStyles.None, CultureInfo.InvariantCulture, out int number) ? number : (int?)null;
    }
}
