using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;

using Core.Imports;

using Scriban;
using Scriban.Runtime;
using Scriban.Syntax;

namespace BlockTemplate
{
    /// <summary>
    /// Renders a template that read cleanly, with the values a form filled in, into the text of every
    /// file it makes - or into every problem, never both. Pure: nothing is read from disk and nothing
    /// is written.
    ///
    /// **What the template sees** (stage 1.2, 2026-10-01): <c>header.&lt;field&gt;</c>, or
    /// <c>header["another-value"]</c> for a field that is not a word; every variable bare,
    /// <c>{{ sensors }}</c>; the chosen TIA version's values bare too, <c>{{ FlgNet }}</c>;
    /// <c>types.&lt;id&gt;</c>, each generated type's full name, so the name is written once, in the
    /// CONFIG-JSON; and <c>uid("key")</c> and <c>uid()</c>.
    ///
    /// Each rule below was measured on Scriban 7.5.0 before it was written:
    /// - **Strict**: a name the template uses and nothing defines is an error at its line, where
    ///   Scriban's default renders it as nothing - a typo would otherwise be a silent hole in a block.
    ///   A member that is not there is one too, <c>header.nmae</c> included.
    /// - **An empty value is null.** Scriban holds an empty string true, so <c>{{ if header.namespace }}</c>
    ///   took the first branch with the field left empty - the opposite of what the maintainer's own
    ///   template means. Null is false, and renders as nothing.
    /// - **Numbers in the invariant culture**: 0.5 is <c>0.5</c> on a Spanish Windows too.
    /// - **In SimaticML, every value is escaped for XML** - a description holding <c>&lt;</c> or
    ///   <c>&amp;</c> must not break the document. The values are escaped, not the output, so the
    ///   template's own markup is untouched and a value joined to another is escaped once. Other
    ///   formats take values as they are.
    /// - **The values are read-only**: a template may make variables of its own, and may not change
    ///   what the form said.
    /// - **A fresh context per file**, and no template loader, so <c>include</c> reaches nothing.
    ///   Reusing a context is what Scriban's one critical advisory needed, fixed in 7.0.0.
    /// </summary>
    public static class TemplateRenderer
    {
        public const string HeaderName = "header";
        public const string TypesName = "types";
        public const string UidName = "uid";

        /// <summary>
        /// Where <c>uid()</c> starts counting: the first UId TIA gives a part of a network. The
        /// numbers only have to be unique while rendering; renumbering in TIA's order is stage 1.3's.
        /// </summary>
        public const int FirstUid = 21;

        private const string NameField = "name";

        public static TemplateRender Render(Template template, TemplateValues values)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            if (values == null) throw new ArgumentNullException(nameof(values));

            List<TemplateProblem> problems = new List<TemplateProblem>();
            Model model = Resolve(template, values, problems);
            if (problems.Count > 0) return new TemplateRender(null, problems);

            List<RenderedFile> files = new List<RenderedFile>();

            files.Add(RenderFile(template.Main, null, model.Name, model, problems));
            foreach (TemplatePart part in template.Parts)
                files.Add(RenderFile(part.File, part.Type, model.Types[part.Type.Id], model, problems));

            return problems.Count > 0 ? new TemplateRender(null, problems) : new TemplateRender(files, problems);
        }

        /// <summary>
        /// The values held against the CONFIG-JSON: every one there, of its type and inside its
        /// range, or a problem naming it - all of them, not the first.
        /// </summary>
        private static Model Resolve(Template template, TemplateValues values, List<TemplateProblem> problems)
        {
            TemplateConfig config = template.Config;
            string file = Path.GetFileName(template.ConfigPath);
            Model model = new Model();

            foreach (string key in values.Header.Keys.Where(k => !config.Header.Contains(k, StringComparer.OrdinalIgnoreCase)))
                problems.Add(new TemplateProblem(file, null, "The values name a header field '" + key + "' it does not have. " +
                                                             "Its fields are " + Listed(config.Header) + "."));

            foreach (string field in config.Header)
            {
                values.Header.TryGetValue(field, out string value);
                model.Header.Add(new KeyValuePair<string, string>(field, value ?? string.Empty));
            }

            model.Name = model.Header.Where(h => h.Key == NameField).Select(h => h.Value).FirstOrDefault();

            if (config.GeneratedTypes.Count > 0 && string.IsNullOrWhiteSpace(model.Name))
                problems.Add(new TemplateProblem(file, null, "header.name is empty, and the generated types are named after it."));

            foreach (GeneratedType type in config.GeneratedTypes)
                model.Types[type.Id] = type.NameFor(model.Name ?? string.Empty);

            foreach (string key in values.Variables.Keys.Where(k => !config.Variables.Any(v => string.Equals(v.Id, k, StringComparison.OrdinalIgnoreCase))))
                problems.Add(new TemplateProblem(file, null, "The values name a variable '" + key + "' it does not have."));

            foreach (TemplateVariable variable in config.Variables)
            {
                object value = values.Variables.TryGetValue(variable.Id, out object given) ? given : variable.Default;
                object resolved = Variable(variable, value, file, problems);
                if (resolved != null) model.Values[variable.Id] = resolved;
            }

            if (config.TiaVersions.Count > 0)
            {
                TiaVersion version = config.TiaVersions.FirstOrDefault(v => v.Version == values.TiaVersion);

                if (version == null)
                    problems.Add(new TemplateProblem(file, null, (string.IsNullOrEmpty(values.TiaVersion)
                                                                     ? "Say which TIA Portal version to render for"
                                                                     : "It has nothing for TIA Portal V" + values.TiaVersion) +
                                                                 ": it lists " + Listed(config.TiaVersions.Select(v => v.Version)) + "."));
                else
                    foreach (TiaVersionVariable value in version.Variables)
                        model.Values[value.Id] = value.Value;
            }

            return model;
        }

        /// <summary>A variable's value as its type says, or null with the problem said.</summary>
        private static object Variable(TemplateVariable variable, object value, string file, List<TemplateProblem> problems)
        {
            string at = "Variable '" + variable.Id + "'";

            if (value == null)
            {
                problems.Add(new TemplateProblem(file, null, at + " has no value."));
                return null;
            }

            switch (variable.Type)
            {
                case TemplateVariableType.List:
                    List<string> entries = value is string || !(value is IEnumerable enumerable)
                        ? null
                        : enumerable.Cast<object>().Select(e => e as string).ToList();

                    if (entries == null || entries.Any(e => e == null))
                    {
                        problems.Add(new TemplateProblem(file, null, at + " must be a list of texts."));
                        return null;
                    }

                    if ((variable.Min.HasValue && entries.Count < variable.Min.Value) || (variable.Max.HasValue && entries.Count > variable.Max.Value))
                    {
                        problems.Add(new TemplateProblem(file, null, at + " holds " + entries.Count + " entries, outside its own " +
                                                                     ConfigParser.Range(variable.Min, variable.Max) + "."));
                        return null;
                    }

                    return entries;

                case TemplateVariableType.Bool:
                    if (value is bool) return value;
                    problems.Add(new TemplateProblem(file, null, at + " must be true or false."));
                    return null;

                case TemplateVariableType.Numeric:
                    decimal? number = Number(value);
                    if (number == null)
                    {
                        problems.Add(new TemplateProblem(file, null, at + " must be a number."));
                        return null;
                    }

                    if ((variable.Min.HasValue && number.Value < variable.Min.Value) || (variable.Max.HasValue && number.Value > variable.Max.Value))
                    {
                        problems.Add(new TemplateProblem(file, null, at + " is " + ConfigParser.Show(number.Value) + ", outside its own " +
                                                                     ConfigParser.Range(variable.Min, variable.Max) + "."));
                        return null;
                    }

                    return number.Value;

                default:
                    if (value is string) return value;
                    problems.Add(new TemplateProblem(file, null, at + " must be a text."));
                    return null;
            }
        }

        /// <summary>
        /// A decimal, or a whole number made one. Never a <see cref="double"/>: the CONFIG-JSON keeps
        /// every digit a number is written with, and a double would not.
        /// </summary>
        private static decimal? Number(object value)
        {
            switch (value)
            {
                case decimal exact: return exact;
                case int whole: return whole;
                case long whole: return whole;
                case short whole: return whole;
                case byte whole: return whole;
                case uint whole: return whole;
                case ulong whole: return whole;
                case ushort whole: return whole;
                case sbyte whole: return whole;
                default: return null;
            }
        }

        /// <summary>
        /// One file and its <c>.s7res</c>, rendered with one <c>uid()</c> between them: a SIMATIC SD
        /// pair links its texts by id, so the two are one document as far as numbering goes.
        /// </summary>
        private static RenderedFile RenderFile(TemplateFile source, GeneratedType type, string name, Model model, List<TemplateProblem> problems)
        {
            Uids uids = new Uids();

            string text = Render(source.Text, Path.GetFileName(source.Path), model, source.Format == ImportFormat.SimaticMl, uids, problems);
            string companion = source.CompanionText == null
                ? null
                : Render(source.CompanionText, Path.GetFileName(source.Companion), model, false, uids, problems);

            if (text == null || (source.CompanionText != null && companion == null)) return null;

            return new RenderedFile(source, type, string.IsNullOrEmpty(name) ? null : name, text, companion);
        }

        private static string Render(string text, string file, Model model, bool xml, Uids uids, List<TemplateProblem> problems)
        {
            Scriban.Template parsed = Scriban.Template.Parse(text, file);

            if (parsed.HasErrors)
            {
                foreach (Scriban.Parsing.LogMessage message in parsed.Messages.Where(m => m.Type == Scriban.Parsing.ParserMessageType.Error))
                    problems.Add(new TemplateProblem(file, message.Span.Start.Line + 1, message.Message));
                return null;
            }

            TemplateContext context = new TemplateContext
            {
                StrictVariables = true,
                EnableRelaxedMemberAccess = false,
                TemplateLoader = null
            };

            context.PushCulture(CultureInfo.InvariantCulture);
            context.PushGlobal(model.Script(xml, uids));
            context.PushGlobal(new ScriptObject());   // where the template's own assignments land

            try
            {
                return parsed.Render(context);
            }
            catch (ScriptRuntimeException failed)
            {
                problems.Add(new TemplateProblem(file, failed.Span.Start.Line + 1, failed.OriginalMessage));
            }
            catch (Exception failed)
            {
                problems.Add(new TemplateProblem(file, null, "It could not be rendered: " + failed.Message));
            }

            return null;
        }

        private static string Listed(IEnumerable<string> names) => string.Join(", ", names);

        /// <summary>The values a template is rendered with, once they have been held against its CONFIG-JSON.</summary>
        private sealed class Model
        {
            /// <summary>The header's fields, in the CONFIG-JSON's order and spelling.</summary>
            public List<KeyValuePair<string, string>> Header { get; } = new List<KeyValuePair<string, string>>();

            /// <summary>The variables' and the chosen TIA version's values, by name.</summary>
            public Dictionary<string, object> Values { get; } = new Dictionary<string, object>(StringComparer.Ordinal);

            /// <summary>Each generated type's full name, by id.</summary>
            public Dictionary<string, string> Types { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            public string Name { get; set; }

            /// <summary>What one file sees, its values escaped for XML when it is SimaticML.</summary>
            public ScriptObject Script(bool xml, Uids uids)
            {
                ScriptObject header = new ScriptObject();
                foreach (KeyValuePair<string, string> field in Header) header.Add(field.Key, Text(field.Value, xml));
                header.IsReadOnly = true;

                ScriptObject types = new ScriptObject();
                foreach (KeyValuePair<string, string> type in Types) types.Add(type.Key, Text(type.Value, xml));
                types.IsReadOnly = true;

                ScriptObject global = new ScriptObject
                {
                    { HeaderName, header },
                    { TypesName, types }
                };

                foreach (KeyValuePair<string, object> value in Values) global.Add(value.Key, Value(value.Value, xml));

                global.Import(UidName, new Func<string, int>(uids.Next));
                global.IsReadOnly = true;
                return global;
            }

            private static object Value(object value, bool xml)
            {
                switch (value)
                {
                    case string text: return Text(text, xml);
                    case List<string> list:
                        return new ReadOnlyList(list.Select(entry => xml ? SecurityElement.Escape(entry) : entry));
                    default: return value;
                }
            }

            /// <summary>Empty is null, so a template's <c>if</c> reads it as false.</summary>
            private static string Text(string value, bool xml) =>
                string.IsNullOrEmpty(value) ? null : xml ? SecurityElement.Escape(value) : value;
        }

        /// <summary>
        /// A list the template reads and cannot change. The object holding it being read-only
        /// protects the name, not the entries, and <see cref="ScriptArray.IsReadOnly"/> protects
        /// members, not entries either: <c>{{ sensors[0] = "x" }}</c> changed the list through both -
        /// Codex's review of stage 1.2 found it, and the second was measured (2026-10-01).
        ///
        /// **Scriban writes an entry through <see cref="IList"/>**, which <c>ScriptArray</c>
        /// implements explicitly - and an explicit implementation cannot be overridden, which is
        /// why overriding the indexer alone changed nothing, also measured. Naming
        /// <see cref="IList"/> again here re-implements it, and that is what reaches the write.
        /// </summary>
        private sealed class ReadOnlyList : ScriptArray, IList
        {
            private readonly bool _filled;

            public ReadOnlyList(IEnumerable<string> entries)
            {
                foreach (string entry in entries) base.Add(entry);
                _filled = true;
                IsReadOnly = true;
            }

            public override object this[int index]
            {
                get => base[index];
                set => Refuse();
            }

            public override void Add(object item)
            {
                if (_filled) Refuse();
                base.Add(item);
            }

            public override void Insert(int index, object item) => Refuse();

            public override void RemoveAt(int index) => Refuse();

            public override bool Remove(object item)
            {
                Refuse();
                return false;
            }

            public override void Clear() => Refuse();

            object IList.this[int index]
            {
                get => base[index];
                set => Refuse();
            }

            int IList.Add(object value)
            {
                Refuse();
                return -1;
            }

            void IList.Insert(int index, object value) => Refuse();

            void IList.Remove(object value) => Refuse();

            void IList.RemoveAt(int index) => Refuse();

            void IList.Clear() => Refuse();

            bool IList.IsReadOnly => true;

            bool IList.IsFixedSize => true;

            bool IList.Contains(object value) => Contains(value);

            int IList.IndexOf(object value) => IndexOf(value);

            int ICollection.Count => Count;

            object ICollection.SyncRoot => this;

            bool ICollection.IsSynchronized => false;

            void ICollection.CopyTo(Array array, int index)
            {
                for (int i = 0; i < Count; i++) array.SetValue(base[i], index + i);
            }

            IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<object>)this).GetEnumerator();

            private static void Refuse() =>
                throw new InvalidOperationException("The list is read-only: what the form filled in cannot be changed by the template.");
        }

        /// <summary>
        /// <c>uid("key")</c> - the same number for the same key, within one file - and <c>uid()</c>,
        /// a number of its own each time. A method with a default rather than a lambda: Scriban
        /// reads the parameters of the method a delegate points at, and a lambda's has no default,
        /// which made <c>uid()</c> an error - measured.
        /// </summary>
        private sealed class Uids
        {
            private readonly Dictionary<string, int> _keys = new Dictionary<string, int>(StringComparer.Ordinal);
            private int _next = FirstUid;

            public int Next(string key = null)
            {
                if (key == null) return _next++;
                if (!_keys.TryGetValue(key, out int number))
                {
                    number = _next++;
                    _keys[key] = number;
                }

                return number;
            }
        }
    }
}
