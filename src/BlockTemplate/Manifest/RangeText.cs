using System.Globalization;

namespace BlockTemplate.Manifest
{
    /// <summary>
    /// How a number and a variable's range are written in a problem - one wording, whether the
    /// manifest's own default is out of range or a form's value is.
    /// </summary>
    internal static class RangeText
    {
        public static string Show(decimal value) => value.ToString(CultureInfo.InvariantCulture);

        public static string Of(decimal? min, decimal? max) =>
            min.HasValue && max.HasValue ? "range of " + Show(min.Value) + " to " + Show(max.Value)
            : min.HasValue ? "minimum of " + Show(min.Value)
            : "maximum of " + Show(max.Value);

        /// <summary>Whether <paramref name="value"/> is outside the range, an absent bound being no bound.</summary>
        public static bool Outside(decimal value, decimal? min, decimal? max) =>
            (min.HasValue && value < min.Value) || (max.HasValue && value > max.Value);
    }
}
