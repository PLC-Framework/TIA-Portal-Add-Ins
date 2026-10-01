using System;
using System.Collections.Generic;
using System.Linq;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BlockTemplate.Manifest.Json
{
    /// <summary>
    /// The problems of one manifest, each said at its line, and the questions of shape every
    /// section asks of its JSON - an unknown key, a list that is not one, an entry that is not an
    /// object. Knows the JSON and the file, and nothing of what a manifest means.
    /// </summary>
    internal sealed class JsonProblems
    {
        /// <summary>
        /// What Json.NET will read as a number, hex included - so a <c>0x10</c> is found whole and
        /// refused as what it is, rather than as a <c>10</c> that came back 16.
        /// </summary>
        private const string NumberCharacters = "0123456789.eE+-xXabcdfABCDF";

        private readonly string _file;
        private readonly List<TemplateProblem> _problems;
        private readonly string[] _lines;

        public JsonProblems(string file, List<TemplateProblem> problems, string json)
        {
            _file = file;
            _problems = problems;
            _lines = (json ?? string.Empty).Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
        }

        public void Add(JToken at, string message)
        {
            IJsonLineInfo line = at;
            Add(line != null && line.HasLineInfo() ? line.LineNumber : (int?)null, message);
        }

        public void Add(int? line, string message) => _problems.Add(new TemplateProblem(_file, line, message));

        /// <summary>
        /// A number as it is written in the JSON, or null when it cannot be found. Json.NET puts a
        /// value's position just past its last character - measured on 13.0.4 - so the literal is
        /// what runs back from there over the characters a number is made of.
        /// </summary>
        public string Literal(JToken token)
        {
            IJsonLineInfo info = token;
            if (info == null || !info.HasLineInfo() || info.LineNumber < 1 || info.LineNumber > _lines.Length) return null;

            string line = _lines[info.LineNumber - 1];
            int end = Math.Min(info.LinePosition, line.Length);
            int start = end;
            while (start > 0 && NumberCharacters.IndexOf(line[start - 1]) >= 0) start--;

            return start < end ? line.Substring(start, end - start) : null;
        }

        /// <summary>
        /// Every key of <paramref name="entry"/> the format does not know, said. **Never ignored**: a
        /// <c>sufix</c> that went unnoticed would name every generated type without its suffix, and
        /// nothing downstream could tell.
        /// </summary>
        public void Keys(JObject entry, IReadOnlyList<string> known, string at)
        {
            foreach (JProperty property in entry.Properties())
            {
                if (known.Contains(property.Name, StringComparer.Ordinal)) continue;

                Add(property, "'" + property.Name + "' is not a key of " + at + ". Its keys are " +
                              string.Join(", ", known) + ".");
            }
        }

        /// <summary>An optional list: null when absent, or when it is not a list - said.</summary>
        public JArray List(JToken token, string at, string shape)
        {
            if (ManifestJson.Absent(token)) return null;

            JArray array = token as JArray;
            if (array == null) Add(token, at + " must be " + shape + ".");
            return array;
        }

        public JObject Entry(JToken token, string at)
        {
            JObject entry = token as JObject;
            if (entry == null) Add(token, at + " must be an object, { ... }.");
            return entry;
        }
    }
}
