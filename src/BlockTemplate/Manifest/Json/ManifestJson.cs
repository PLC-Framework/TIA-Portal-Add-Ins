using System;
using System.Collections.Generic;
using System.Linq;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BlockTemplate.Manifest.Json
{
    /// <summary>
    /// A manifest's text read into a JSON tree. **Tolerant in how it is written, strict in what it
    /// says** (the maintainer's rule): comments and trailing commas are accepted - the file is
    /// written by hand - and <c>undefined</c>, a key given twice and text after the closing brace
    /// are refused, each at its line.
    /// </summary>
    internal static class ManifestJson
    {
        private static readonly JsonLoadSettings Settings = new JsonLoadSettings
        {
            CommentHandling = CommentHandling.Ignore,
            LineInfoHandling = LineInfoHandling.Load,
            DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
        };

        /// <summary>
        /// The tree, or null - said - when the text does not read or holds nothing but
        /// <c>undefined</c>.
        ///
        /// **An <c>undefined</c> is said and read as null**, so the checks after it still run: every
        /// problem, not the first. Left in place it would be said a second time by whatever expected
        /// a value there. Codex's review of stage 1.1 found the read stopping at it.
        /// </summary>
        public static JToken Read(string json, JsonProblems problems)
        {
            JToken root;
            try
            {
                root = Load(json);
            }
            catch (JsonReaderException failed)
            {
                problems.Add(failed.LineNumber > 0 ? failed.LineNumber : (int?)null, "The manifest does not read: " + Plain(failed.Message));
                return null;
            }

            foreach (JToken token in Tokens(root).Where(t => t.Type == JTokenType.Undefined).ToList())
            {
                problems.Add(token, "undefined is not a value. Write null, or leave the key out.");
                if (token != root) token.Replace(JValue.CreateNull());
            }

            return root.Type == JTokenType.Undefined ? null : root;
        }

        /// <summary>A key left out and a key written as <c>null</c> mean the same.</summary>
        public static bool Absent(JToken token) => token == null || token.Type == JTokenType.Null;

        /// <summary>
        /// The JSON as a tree, read the way a manifest means it rather than Json.NET's defaults -
        /// both of which change a value behind its author's back, and Codex's review of stage 1.1
        /// found the first:
        ///
        /// - **A number with a fraction is read as a <see cref="decimal"/>**, not a <see cref="double"/>
        ///   converted afterwards: <c>0.1234567890123456789012345678</c> came back cut to fifteen
        ///   digits, and that is the number the template would then have written.
        /// - **A string that looks like a date stays a string.** Json.NET turns one into a
        ///   <c>DateTime</c> unless told not to, and a default of <c>"2026-10-01T00:00:00"</c> would
        ///   then have been refused as not being a string.
        ///
        /// Text after the closing brace is refused by the reader itself, as <c>JToken.Parse</c> would.
        /// </summary>
        private static JToken Load(string json)
        {
            using (JsonTextReader reader = new JsonTextReader(new System.IO.StringReader(json)))
            {
                reader.FloatParseHandling = FloatParseHandling.Decimal;
                reader.DateParseHandling = DateParseHandling.None;

                JToken root = JToken.ReadFrom(reader, Settings);
                while (reader.Read()) { }   // a comment after the JSON is fine; anything else throws
                return root;
            }
        }

        /// <summary>
        /// Json.NET's message without its own "Path '…', line n, position m.", which the problem
        /// says better on its own.
        /// </summary>
        private static string Plain(string message)
        {
            int path = message.IndexOf(" Path '", StringComparison.Ordinal);
            if (path < 0) path = message.IndexOf(", line ", StringComparison.Ordinal);
            return path < 0 ? message : message.Substring(0, path).TrimEnd('.', ',', ' ') + ".";
        }

        private static IEnumerable<JToken> Tokens(JToken token)
        {
            yield return token;

            JContainer container = token as JContainer;
            if (container == null) yield break;

            foreach (JToken child in container.Descendants())
                yield return child;
        }
    }
}
