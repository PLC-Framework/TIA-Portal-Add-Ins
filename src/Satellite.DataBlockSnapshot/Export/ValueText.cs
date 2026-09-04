using System;
using System.Globalization;

namespace Satellite.DataBlockSnapshot.Export
{
    /// <summary>
    /// Renders a value as the text that goes inside a worksheet cell.
    ///
    /// Always invariant, and not as a preference: the spreadsheet format stores a
    /// numeric cell as an invariant string in the XML and lets the reader's own locale
    /// decide how to display it. Writing 6,785 there would produce a file Excel refuses
    /// to read as a number on any machine, including the one that wrote it.
    /// </summary>
    internal static class ValueText
    {
        public static string Render(object value)
        {
            if (value == null) return string.Empty;

            // Lower case, so the text matches what the JSON export writes for the same
            // value and neither file needs a reader that knows about .NET conventions.
            if (value is bool flag) return flag ? "true" : "false";

            if (value is double number)
                return number.ToString("R", CultureInfo.InvariantCulture);

            if (value is float single)
                return single.ToString("R", CultureInfo.InvariantCulture);

            if (value is IFormattable formattable)
                return formattable.ToString(null, CultureInfo.InvariantCulture);

            return value.ToString();
        }

        /// <summary>ISO-8601 with the offset, so a capture keeps its meaning off site.</summary>
        public static string Timestamp(DateTime moment) =>
            moment.ToString("yyyy-MM-dd'T'HH:mm:ss.fffK", CultureInfo.InvariantCulture);
    }
}
