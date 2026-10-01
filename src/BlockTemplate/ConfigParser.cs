using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BlockTemplate
{
    /// <summary>
    /// A template's CONFIG-JSON - its <c>.json</c> - read into a <see cref="TemplateConfig"/> or into
    /// every problem it has, each at its line of that file.
    ///
    /// **Tolerant in how it is written, strict in what it says** (the maintainer's rule). Comments
    /// and trailing commas are accepted - the file is written by hand - but <c>undefined</c> is
    /// refused, and so is a key given twice. **A key nobody knows is an error, never ignored**: a
    /// <c>sufix</c> that went unnoticed would name every generated type without its suffix, and
    /// nothing downstream could tell.
    /// </summary>
    internal static class ConfigParser
    {
        /// <summary>
        /// Names a variable may not take, because the template itself uses them: <c>header</c> holds
        /// the header's fields and <c>uid</c> numbers the parts of a network.
        /// </summary>
        public static readonly IReadOnlyList<string> Reserved = new[] { "header", "uid" };

        /// <summary>
        /// Names Scriban keeps for itself, spelled exactly - <c>If</c> is a name like any other.
        /// **Measured on Scriban 7.5.0**, not recalled: each rendered as <c>{{ name }}</c> with a value
        /// of that name. Its keywords and <c>tablerow</c>, <c>this</c>, <c>true</c>, <c>false</c> and
        /// <c>null</c> did not parse, threw, or rendered something else; its built-in objects did
        /// render, and would hide <c>string.upcase</c> and the like from the template that named a
        /// variable after them. Codex's twelfth review found the first kind passing (2026-10-01).
        /// </summary>
        private static readonly string[] ScribanWords =
        {
            "if", "else", "end", "for", "case", "when", "while", "break", "continue", "func", "import", "readonly",
            "with", "capture", "ret", "wrap", "do", "tablerow", "this", "true", "false", "null",
            "array", "blank", "date", "empty", "html", "include", "include_join", "math", "object", "regex", "string", "timespan"
        };

        private static readonly string[] RootKeys = { "header", "tiaVersions", "variables", "generatedTypes" };
        private static readonly string[] VariableKeys = { "id", "type", "min", "max", "default" };
        private static readonly string[] VersionKeys = { "version", "variables" };
        private static readonly string[] VersionVariableKeys = { "id", "value" };
        private static readonly string[] TypeKeys = { "id", "prefix", "suffix" };

        /// <summary>A name the template writes bare - <c>{{ sensors }}</c> - so it has to be a word.</summary>
        private static readonly Regex Identifier = new Regex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);

        /// <summary>
        /// A TIA Portal major version, spelled one way only: <c>"020"</c> would be a second entry for
        /// V20 and the one the executable never asks for. Codex's thirteenth review (2026-10-01).
        /// </summary>
        private static readonly Regex TiaMajor = new Regex("^[1-9][0-9]*$", RegexOptions.CultureInvariant);

        private static readonly JsonLoadSettings Settings = new JsonLoadSettings
        {
            CommentHandling = CommentHandling.Ignore,
            LineInfoHandling = LineInfoHandling.Load,
            DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
        };

        /// <summary>
        /// The JSON as a tree, read the way a template means it rather than Json.NET's defaults -
        /// both of which change a value behind its author's back, and Codex's review of this stage
        /// found the first (2026-10-01):
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
        /// The CONFIG-JSON as far as it reads - **null only when there is no object to read at all**.
        /// What did read comes back beside the problems, because the reader holds the sub-templates
        /// against it: a missing one is worth saying on the same read as an unknown key, and Codex's
        /// fourth review found it waiting for the key to be fixed (2026-10-01). It is never handed to
        /// anybody with problems beside it - <see cref="TemplateReader"/> returns no template then.
        /// </summary>
        /// <param name="typesWhole">
        /// Every <c>generatedTypes</c> entry read into a type. Only then can a sub-template no entry
        /// describes be called a stray: otherwise it may be the one a broken entry meant.
        /// </param>
        public static TemplateConfig Parse(string json, string file, List<TemplateProblem> problems, out bool typesWhole)
        {
            Reading reading = new Reading(file, problems, json);
            typesWhole = false;

            JToken root;
            try
            {
                root = Load(json);
            }
            catch (JsonReaderException failed)
            {
                problems.Add(new TemplateProblem(file, failed.LineNumber > 0 ? failed.LineNumber : (int?)null,
                                                 "The CONFIG-JSON does not read: " + Plain(failed.Message)));
                return null;
            }

            // Said, then read as null: the rest of the file is still worth checking - every problem,
            // not the first - and an undefined left in place would be said a second time by whatever
            // expected a value there. Codex's review of this stage found it stopping here (2026-10-01).
            foreach (JToken token in Tokens(root).Where(t => t.Type == JTokenType.Undefined).ToList())
            {
                reading.Add(token, "undefined is not a value. Write null, or leave the key out.");
                if (token != root) token.Replace(JValue.CreateNull());
            }

            if (root.Type == JTokenType.Undefined) return null;

            JObject top = root as JObject;
            if (top == null)
            {
                reading.Add(root, "The CONFIG-JSON must be an object: { \"header\": [...], \"variables\": [...], ... }.");
                return null;
            }

            reading.Keys(top, RootKeys, "the CONFIG-JSON");

            List<string> header = Header(reading, top["header"]);
            List<TemplateVariable> variables = Variables(reading, top["variables"], out HashSet<string> names);
            List<TiaVersion> versions = Versions(reading, top["tiaVersions"], names);
            List<GeneratedType> types = Types(reading, top["generatedTypes"], header, out typesWhole);

            return new TemplateConfig(header, versions, variables, types);
        }

        private static List<string> Header(Reading reading, JToken token)
        {
            List<string> names = new List<string>();
            JArray array = reading.List(token, "header", "a list of field names: [\"author\", \"name\", ...]");
            if (array == null) return names;

            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < array.Count; i++)
            {
                JToken item = array[i];
                string at = "header[" + i + "]";

                if (item.Type != JTokenType.String)
                {
                    reading.Add(item, at + " must be a field name in quotes.");
                    continue;
                }

                string name = (string)item;

                if (string.IsNullOrWhiteSpace(name))
                    reading.Add(item, at + " is empty.");
                else if (!seen.Add(name))
                    reading.Add(item, at + " '" + name + "' is in the list already.");
                else
                    names.Add(name);
            }

            return names;
        }

        /// <param name="names">
        /// Every id a variable took, whatever else was wrong with it - so a TIA value named alike is
        /// said on the same read rather than once the variable's default is fixed. Codex's third
        /// review found the collision waiting for that (2026-10-01).
        /// </param>
        private static List<TemplateVariable> Variables(Reading reading, JToken token, out HashSet<string> names)
        {
            List<TemplateVariable> variables = new List<TemplateVariable>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            names = seen;

            JArray array = reading.List(token, "variables", "a list of { \"id\": ..., \"type\": ... }");
            if (array == null) return variables;

            for (int i = 0; i < array.Count; i++)
            {
                string at = "variables[" + i + "]";
                JObject entry = reading.Entry(array[i], at);
                if (entry == null) continue;

                reading.Keys(entry, VariableKeys, at);

                string id = reading.Name(entry, "id", at);
                bool unique = id != null && seen.Add(id);
                if (id != null && !unique)
                    reading.Add(entry["id"], at + ".id '" + id + "' is another variable's already.");

                TemplateVariableType? type = Type(reading, entry, at);
                if (type == null) continue;

                // The rest is checked whatever became of the id: a duplicate with a bad default is two
                // problems, and saying one per read is what this reader exists not to do.
                TemplateVariable variable = Variable(reading, entry, at, id ?? string.Empty, type.Value);
                if (variable != null && unique) variables.Add(variable);
            }

            return variables;
        }

        private static TemplateVariableType? Type(Reading reading, JObject entry, string at)
        {
            JToken token = entry["type"];

            if (Absent(token))
            {
                reading.Add(entry, at + " has no type: list, bool, numeric or string.");
                return null;
            }

            switch (token.Type == JTokenType.String ? (string)token : null)
            {
                case "list": return TemplateVariableType.List;
                case "bool": return TemplateVariableType.Bool;
                case "numeric": return TemplateVariableType.Numeric;
                case "string": return TemplateVariableType.String;
            }

            reading.Add(token, at + ".type must be list, bool, numeric or string, spelled so.");
            return null;
        }

        private static TemplateVariable Variable(Reading reading, JObject entry, string at, string id, TemplateVariableType type)
        {
            JToken minToken = entry["min"];
            JToken maxToken = entry["max"];
            JToken defaultToken = entry["default"];

            if (type == TemplateVariableType.Bool || type == TemplateVariableType.String)
            {
                if (!Absent(minToken)) reading.Add(minToken, at + ".min means nothing for a " + Spelled(type) + ": only a list and a number have one.");
                if (!Absent(maxToken)) reading.Add(maxToken, at + ".max means nothing for a " + Spelled(type) + ": only a list and a number have one.");

                if (type == TemplateVariableType.Bool)
                {
                    if (Absent(defaultToken)) return new TemplateVariable(id, type, null, null, false);
                    if (defaultToken.Type == JTokenType.Boolean) return new TemplateVariable(id, type, null, null, (bool)defaultToken);
                    reading.Add(defaultToken, at + ".default must be true or false.");
                    return null;
                }

                if (Absent(defaultToken)) return new TemplateVariable(id, type, null, null, string.Empty);
                if (defaultToken.Type == JTokenType.String) return new TemplateVariable(id, type, null, null, (string)defaultToken);
                reading.Add(defaultToken, at + ".default must be a string in quotes.");
                return null;
            }

            bool list = type == TemplateVariableType.List;
            decimal? min = Bound(reading, minToken, at + ".min", list);
            decimal? max = Bound(reading, maxToken, at + ".max", list);

            // Said, and the default still checked for being the right kind of value - only not against
            // a range that does not exist.
            bool inverted = min.HasValue && max.HasValue && min.Value > max.Value;
            if (inverted)
                reading.Add(maxToken, at + ".max is " + Show(max.Value) + ", below its min of " + Show(min.Value) + ".");

            if (list)
            {
                List<string> entries = new List<string>();

                if (!Absent(defaultToken))
                {
                    JArray array = defaultToken as JArray;
                    if (array == null || array.Any(item => item.Type != JTokenType.String))
                    {
                        reading.Add(defaultToken, at + ".default must be a list of strings: [\"first\", \"second\"].");
                        return null;
                    }

                    entries.AddRange(array.Select(item => (string)item));
                }

                if (!inverted && ((min.HasValue && entries.Count < min.Value) || (max.HasValue && entries.Count > max.Value)))
                {
                    reading.Add(Absent(defaultToken) ? (JToken)entry : defaultToken,
                                at + ".default holds " + entries.Count + " entries, outside its own " + Range(min, max) + ".");
                    return null;
                }

                return inverted ? null : new TemplateVariable(id, type, min, max, entries.AsReadOnly());
            }

            decimal value;
            if (Absent(defaultToken))
            {
                value = min ?? 0m;
            }
            else
            {
                decimal? number = Number(reading, defaultToken, at + ".default");
                if (number == null) return null;
                value = number.Value;
            }

            if (!inverted && ((min.HasValue && value < min.Value) || (max.HasValue && value > max.Value)))
            {
                reading.Add(Absent(defaultToken) ? (JToken)entry : defaultToken,
                            at + ".default is " + Show(value) + ", outside its own " + Range(min, max) + ".");
                return null;
            }

            return inverted ? null : new TemplateVariable(id, type, min, max, value);
        }

        private static List<TiaVersion> Versions(Reading reading, JToken token, HashSet<string> taken)
        {
            List<TiaVersion> versions = new List<TiaVersion>();
            JArray array = reading.List(token, "tiaVersions", "a list of { \"version\": \"20\", \"variables\": [...] }");
            if (array == null) return versions;

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            List<JToken> entries = new List<JToken>();
            List<string> labels = new List<string>();
            List<List<string>> names = new List<List<string>>();

            for (int i = 0; i < array.Count; i++)
            {
                string at = "tiaVersions[" + i + "]";
                JObject entry = reading.Entry(array[i], at);
                if (entry == null) continue;

                reading.Keys(entry, VersionKeys, at);

                string version = TiaMajorOf(reading, entry, at);
                if (version != null && !seen.Add(version))
                {
                    reading.Add(entry["version"], at + ".version " + version + " is listed already.");
                    version = null;
                }

                List<TiaVersionVariable> values = VersionValues(reading, entry["variables"], at, taken, out List<string> named);

                // An entry whose version did not read is still held to the names the others set, and
                // still says which names they share: the label and the names are two facts. Codex's
                // ninth and tenth reviews found each waiting on the label being fixed (2026-10-01).
                entries.Add(entry);
                labels.Add(version == null ? at : "tiaVersions entry " + version);
                names.Add(named);

                if (version != null) versions.Add(new TiaVersion(version, values));
            }

            // Every version names the same values, or rendering for one of them leaves a hole where
            // the template expected a value - and Scriban renders an unknown name as nothing at all.
            // **Spelled exactly the same**: Scriban tells schemaVersion from SchemaVersion, so a
            // comparison that did not would pass the very hole it exists to catch. Codex's third
            // review found it (2026-10-01). **By the names each version gives, whatever their
            // values**: a name with a bad value is still set, and the eighth review found a version
            // that left it out waiting for that value to be fixed.
            List<string> all = names.SelectMany(n => n).Distinct(StringComparer.Ordinal).ToList();

            for (int i = 0; i < entries.Count; i++)
            {
                foreach (string id in all.Where(id => !names[i].Contains(id, StringComparer.Ordinal)))
                {
                    string near = names[i].FirstOrDefault(n => string.Equals(n, id, StringComparison.OrdinalIgnoreCase));

                    reading.Add(entries[i], labels[i] + " does not set '" + id +
                                            "', which another version does: rendering for it would leave it empty." +
                                            (near == null ? string.Empty
                                                          : " It sets '" + near + "', and a template reads the two as different names."));
                }
            }

            return versions;
        }

        private static string TiaMajorOf(Reading reading, JObject entry, string at)
        {
            JToken token = entry["version"];

            if (Absent(token))
            {
                reading.Add(entry, at + " has no version: the TIA Portal major version, \"20\" or \"21\".");
                return null;
            }

            // A number is taken as written: Json.NET reads 020 as octal 16, which is not a TIA Portal.
            string version = token.Type == JTokenType.String ? (string)token
                           : token.Type == JTokenType.Integer ? reading.Literal(token) ?? Convert.ToString(((JValue)token).Value, CultureInfo.InvariantCulture)
                           : null;

            if (version != null && token.Type == JTokenType.Integer && !JsonNumber.IsMatch(version)) version = null;

            if (version == null || !TiaMajor.IsMatch(version))
            {
                reading.Add(token, at + ".version must be a TIA Portal major version in digits, such as \"20\", with no leading zero.");
                return null;
            }

            return version;
        }

        /// <param name="named">Every name this version gives a value, whether or not the value read.</param>
        private static List<TiaVersionVariable> VersionValues(Reading reading, JToken token, string at, HashSet<string> taken,
                                                              out List<string> named)
        {
            List<TiaVersionVariable> values = new List<TiaVersionVariable>();
            named = new List<string>();
            JArray array = reading.List(token, at + ".variables", "a list of { \"id\": ..., \"value\": ... }");
            if (array == null) return values;

            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < array.Count; i++)
            {
                string where = at + ".variables[" + i + "]";
                JObject entry = reading.Entry(array[i], where);
                if (entry == null) continue;

                reading.Keys(entry, VersionVariableKeys, where);

                string id = reading.Name(entry, "id", where);
                if (id != null && taken.Contains(id))
                {
                    reading.Add(entry["id"], where + ".id '" + id + "' is the name of a variable too: the template could not tell the two apart.");
                    id = null;
                }
                else if (id != null && !seen.Add(id))
                {
                    reading.Add(entry["id"], where + ".id '" + id + "' is set already for this version.");
                    id = null;
                }

                if (id != null) named.Add(id);

                object value = Scalar(reading, entry, "value", where);
                if (id != null && value != null) values.Add(new TiaVersionVariable(id, value));
            }

            return values;
        }

        /// <param name="whole">Every entry gave an id - see <see cref="Parse"/>.</param>
        private static List<GeneratedType> Types(Reading reading, JToken token, List<string> header, out bool whole)
        {
            List<GeneratedType> types = new List<GeneratedType>();
            JArray array = reading.List(token, "generatedTypes", "a list of { \"id\": ..., \"prefix\": ..., \"suffix\": ... }");
            whole = array != null || Absent(token);
            if (array == null) return types;

            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, int> naming = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < array.Count; i++)
            {
                string at = "generatedTypes[" + i + "]";
                JObject entry = reading.Entry(array[i], at);
                if (entry == null)
                {
                    whole = false;
                    continue;
                }

                reading.Keys(entry, TypeKeys, at);

                string id = reading.FilePart(entry, "id", at);

                // An id given twice is still an id that read: both entries name the same file, so no
                // other file can be the one either meant, and a stray beside them is still said.
                // Codex's thirteenth review found it held back (2026-10-01).
                if (id == null) whole = false;
                else if (!seen.Add(id))
                {
                    reading.Add(entry["id"], at + ".id '" + id + "' names another type already.");
                    id = null;
                }

                string prefix = Text(reading, entry, "prefix", at);
                string suffix = Text(reading, entry, "suffix", at);

                // Two types named alike for every header.name could never both be generated: the
                // second would meet the first in the project. Whatever the case, the conservative
                // reading of a name. Codex's ninth review found two passing (2026-10-01).
                if (prefix != null && suffix != null)
                {
                    string named = prefix + "\0" + suffix;
                    if (naming.TryGetValue(named, out int first))
                        reading.Add(entry, at + " names its type exactly as generatedTypes[" + first + "] does: both would be '" +
                                           prefix + "<name>" + suffix + "'.");
                    else
                        naming.Add(named, i);
                }

                // Kept with its id whatever became of the rest, so its sub-template is still looked
                // for: the id is all the matching reads, and a type with a bad suffix is never handed
                // out - the problem beside it sees to that.
                if (id != null) types.Add(new GeneratedType(id, prefix ?? string.Empty, suffix ?? string.Empty));
            }

            // A type's name is prefix + header.name + suffix, so with no "name" in the header there
            // is nothing to build it from.
            if (array.Count > 0 && !header.Contains("name", StringComparer.Ordinal))
                reading.Add(token, "generatedTypes are named prefix + header.name + suffix, and the header has no \"name\".");

            return types;
        }

        /// <summary>An optional string, empty when absent - or null, with the problem said, when it is not one.</summary>
        private static string Text(Reading reading, JObject entry, string key, string at)
        {
            JToken token = entry[key];
            if (Absent(token)) return string.Empty;
            if (token.Type == JTokenType.String) return (string)token;

            reading.Add(token, at + "." + key + " must be a string in quotes.");
            return null;
        }

        /// <summary>A required string, number or bool - or null, with the problem said.</summary>
        private static object Scalar(Reading reading, JObject entry, string key, string at)
        {
            JToken token = entry[key];

            if (Absent(token))
            {
                reading.Add(entry, at + " has no " + key + ".");
                return null;
            }

            switch (token.Type)
            {
                case JTokenType.String: return (string)token;
                case JTokenType.Boolean: return (bool)token;
                case JTokenType.Integer:
                case JTokenType.Float: return Number(reading, token, at + "." + key);
            }

            reading.Add(token, at + "." + key + " must be a string, a number or true or false.");
            return null;
        }

        /// <summary>
        /// A list's bound is a whole count, never negative; a number's bound is any number. Null when
        /// absent, or when it is not one - said.
        /// </summary>
        private static decimal? Bound(Reading reading, JToken token, string at, bool count)
        {
            if (Absent(token)) return null;

            if (count)
            {
                if (token.Type == JTokenType.Integer)
                {
                    decimal? entries = Number(reading, token, at);
                    if (entries == null) return null;
                    if (entries.Value >= 0) return entries;
                }

                reading.Add(token, at + " must be a whole number of entries, 0 or more.");
                return null;
            }

            return Number(reading, token, at);
        }

        /// <summary>
        /// A number as a <see cref="decimal"/>, or null - said - when it is not one or a decimal
        /// cannot hold it.
        ///
        /// **Through its digits, not through <c>Convert</c>**: an integer past <c>Int64</c> arrives
        /// from Json.NET as a <c>BigInteger</c>, which is not <c>IConvertible</c>, and
        /// <c>Convert.ToDecimal</c> threw <c>InvalidCastException</c> out of a reader that promises
        /// never to throw - Codex's review of this stage found it (2026-10-01).
        /// </summary>
        private static decimal? Number(Reading reading, JToken token, string at)
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
                reading.Add(token, at + " must be a number - and one a decimal can hold.");
                return null;
            }

            // Json.NET reads more than JSON writes and keeps less than a decimal is handed, both
            // without a word, so the literal as written is held against what came back. Codex found
            // each: 0.12345678901234567890123456789 read as ...679 (the sixth review), and 010 read
            // as octal, 8 (the seventh), 2026-10-01.
            string literal = reading.Literal(token);
            if (literal == null) return number;

            if (!JsonNumber.IsMatch(literal))
            {
                reading.Add(token, at + " is written " + literal + ", which is not a number as JSON writes one.");
                return null;
            }

            if (!Same(literal, number))
            {
                reading.Add(token, at + " is written " + literal + ", and a decimal can hold it only as " + Show(number) +
                                   ": write it with fewer digits.");
                return null;
            }

            return number;
        }

        /// <summary>A number as JSON writes one: no leading zero, no hex, no sign but a minus.</summary>
        private static readonly Regex JsonNumber = new Regex(
            @"^-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?$", RegexOptions.CultureInvariant);

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

        private static readonly Regex NumberLiteral = new Regex(
            @"^(?<sign>-)?(?<whole>[0-9]*)(?:\.(?<fraction>[0-9]*))?(?:[eE](?<exponent>[+-]?[0-9]+))?$",
            RegexOptions.CultureInvariant);

        private static bool Absent(JToken token) => token == null || token.Type == JTokenType.Null;

        private static string Spelled(TemplateVariableType type) => type.ToString().ToLowerInvariant();

        private static string Show(decimal value) => value.ToString(CultureInfo.InvariantCulture);

        private static string Range(decimal? min, decimal? max) =>
            min.HasValue && max.HasValue ? "range of " + Show(min.Value) + " to " + Show(max.Value)
            : min.HasValue ? "minimum of " + Show(min.Value)
            : "maximum of " + Show(max.Value);

        /// <summary>
        /// Json.NET's message without its own "Path '…', line n, position m." - which counts lines
        /// from the start of the JSON rather than of the file, and would send the reader elsewhere.
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

        /// <summary>One read: the file, its lines, and the problems so far.</summary>
        private sealed class Reading
        {
            private readonly string _file;
            private readonly List<TemplateProblem> _problems;
            private readonly string[] _lines;

            public Reading(string file, List<TemplateProblem> problems, string json)
            {
                _file = file;
                _problems = problems;
                _lines = (json ?? string.Empty).Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            }

            /// <summary>
            /// What Json.NET will read as a number, hex included - so a <c>0x10</c> is found whole and
            /// refused as what it is, rather than as a <c>10</c> that came back 16.
            /// </summary>
            private const string NumberCharacters = "0123456789.eE+-xXabcdfABCDF";

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

            public void Add(JToken at, string message)
            {
                IJsonLineInfo line = at;
                int? number = line != null && line.HasLineInfo() ? line.LineNumber : (int?)null;
                _problems.Add(new TemplateProblem(_file, number, message));
            }

            /// <summary>Every key of <paramref name="entry"/> the format does not know, said.</summary>
            public void Keys(JObject entry, string[] known, string at)
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
                if (Absent(token)) return null;

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

            /// <summary>
            /// A required name that is the middle of a file name - a generated type's id: anything a
            /// Windows file name takes, but a dot, which would make the name read two ways.
            ///
            /// **Not held to the rules of a variable's name**, and the first version was: a variable
            /// is written bare in the template, a type's id is a file name's part, and
            /// <c>motor-settings</c> is a perfectly good one. Codex's review found it (2026-10-01).
            /// </summary>
            public string FilePart(JObject entry, string key, string at)
            {
                JToken token = entry[key];

                if (Absent(token))
                {
                    Add(entry, at + " has no " + key + ".");
                    return null;
                }

                if (token.Type != JTokenType.String)
                {
                    Add(token, at + "." + key + " must be a name in quotes.");
                    return null;
                }

                string name = (string)token;

                if (name.Trim().Length == 0 || name != name.Trim() || name.IndexOf('.') >= 0 ||
                    name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
                {
                    Add(token, at + "." + key + " '" + name + "' cannot be part of a file name: no dot, " +
                               "no space at either end, and nothing Windows refuses in one.");
                    return null;
                }

                return name;
            }

            /// <summary>A required name the template writes bare, not one of <see cref="Reserved"/>.</summary>
            public string Name(JObject entry, string key, string at)
            {
                JToken token = entry[key];

                if (Absent(token))
                {
                    Add(entry, at + " has no " + key + ".");
                    return null;
                }

                if (token.Type != JTokenType.String)
                {
                    Add(token, at + "." + key + " must be a name in quotes.");
                    return null;
                }

                string name = (string)token;

                if (!Identifier.IsMatch(name))
                {
                    Add(token, at + "." + key + " '" + name + "' is not a name a template can write: " +
                               "a letter or _ first, then letters, digits and _.");
                    return null;
                }

                if (Reserved.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    Add(token, at + "." + key + " '" + name + "' is taken by the template itself.");
                    return null;
                }

                if (ScribanWords.Contains(name, StringComparer.Ordinal))
                {
                    Add(token, at + "." + key + " '" + name + "' is a word Scriban keeps for itself, so a template " +
                               "could not use it as a name.");
                    return null;
                }

                return name;
            }
        }
    }
}
