using System;
using System.Globalization;
using System.Text.RegularExpressions;

using Newtonsoft.Json.Linq;

namespace BlockTemplate.Manifest.Json
{
    /// <summary>
    /// A number read as it was written. Json.NET reads more than JSON writes and keeps less than a
    /// decimal is handed, both without a word, so the literal as written is held against what came
    /// back. Codex found each in its review of stage 1.1: <c>0.12345678901234567890123456789</c>
    /// read as <c>…679</c> (the sixth round), and <c>010</c> read as octal, 8 (the seventh) - and
    /// measured beside it, <c>020</c> is 16, <c>0x10</c> is 16 and <c>-010</c> is -10.
    /// </summary>
    internal static class ExactNumbers
    {
        /// <summary>A number as JSON writes one: no leading zero, no hex, no sign but a minus.</summary>
        private static readonly Regex JsonNumber = new Regex(
            @"^-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?$", RegexOptions.CultureInvariant);

        private static readonly Regex NumberLiteral = new Regex(
            @"^(?<sign>-)?(?<whole>[0-9]*)(?:\.(?<fraction>[0-9]*))?(?:[eE](?<exponent>[+-]?[0-9]+))?$",
            RegexOptions.CultureInvariant);

        /// <summary>
        /// A number as a <see cref="decimal"/>, or null - said - when it is not one, a decimal cannot
        /// hold it, or it is not written as JSON writes a number.
        ///
        /// **Through its digits, not through <c>Convert</c>**: an integer past <c>Int64</c> arrives
        /// from Json.NET as a <c>BigInteger</c>, which is not <c>IConvertible</c>, and
        /// <c>Convert.ToDecimal</c> threw <c>InvalidCastException</c> out of a reader that promises
        /// never to throw - Codex's first review of stage 1.1 found it.
        /// </summary>
        public static decimal? Read(JsonProblems problems, JToken token, string at)
        {
            object value = (token as JValue)?.Value;
            decimal number;

            if (value is decimal exact)
            {
                number = exact;
            }
            else if (token.Type == JTokenType.Integer &&
                     decimal.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.AllowLeadingSign,
                                      CultureInfo.InvariantCulture, out decimal whole))
            {
                number = whole;
            }
            else
            {
                problems.Add(token, at + " must be a number - and one a decimal can hold.");
                return null;
            }

            string literal = problems.Literal(token);
            if (literal == null) return number;

            if (!IsJsonNumber(literal))
            {
                problems.Add(token, at + " is written " + literal + ", which is not a number as JSON writes one.");
                return null;
            }

            if (!Same(literal, number))
            {
                problems.Add(token, at + " is written " + literal + ", and a decimal can hold it only as " +
                                    number.ToString(CultureInfo.InvariantCulture) + ": write it with fewer digits.");
                return null;
            }

            return number;
        }

        public static bool IsJsonNumber(string literal) => JsonNumber.IsMatch(literal);

        /// <summary>
        /// Whether a JSON number literal and a decimal are the same number - compared digit by digit,
        /// trailing zeros and the exponent normalised away, so <c>1.50</c>, <c>15e-1</c> and 1.5 agree.
        /// </summary>
        private static bool Same(string literal, decimal value)
        {
            Match written = NumberLiteral.Match(literal);
            Match held = NumberLiteral.Match(value.ToString(CultureInfo.InvariantCulture));
            if (!written.Success || !held.Success) return true;

            string first = Normalised(written, out bool firstNegative, out long firstExponent);
            string second = Normalised(held, out bool secondNegative, out long secondExponent);

            if (first == null) return false;                       // an exponent too large to count
            if (first.Length == 0 || second.Length == 0) return first.Length == second.Length;
            return first == second && firstExponent == secondExponent && firstNegative == secondNegative;
        }

        /// <summary>
        /// The significant digits, no zero at either end, with the power of ten they are scaled by -
        /// empty for zero, and null when the exponent will not fit a <see cref="long"/>.
        /// </summary>
        private static string Normalised(Match number, out bool negative, out long exponent)
        {
            negative = number.Groups["sign"].Value == "-";
            exponent = 0;

            string fraction = number.Groups["fraction"].Value;
            string digits = (number.Groups["whole"].Value + fraction).TrimStart('0');

            if (number.Groups["exponent"].Success &&
                !long.TryParse(number.Groups["exponent"].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out exponent))
                return digits.Length == 0 ? string.Empty : null;

            string trimmed = digits.TrimEnd('0');
            exponent = exponent - fraction.Length + (digits.Length - trimmed.Length);
            return trimmed;
        }
    }
}
