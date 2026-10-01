using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using BlockTemplate.Manifest;
using BlockTemplate.Reading;

namespace BlockTemplate.Rendering
{
    /// <summary>
    /// The values a form filled in, held against the manifest before anything is rendered: every
    /// one there, of its type and inside its range, or a problem naming it - all of them, not the
    /// first. What passes becomes the <see cref="RenderModel"/>.
    /// </summary>
    internal static class ValuesCheck
    {
        public static RenderModel Resolve(Template template, TemplateValues values, List<TemplateProblem> problems)
        {
            TemplateManifest manifest = template.Manifest;
            string file = Path.GetFileName(template.ManifestPath);
            RenderModel model = new RenderModel();

            foreach (string key in values.Header.Keys.Where(k => !manifest.Header.Contains(k, StringComparer.OrdinalIgnoreCase)))
                problems.Add(new TemplateProblem(file, null, "The values name a header field '" + key + "' it does not have. " +
                                                             "Its fields are " + Listed(manifest.Header) + "."));

            foreach (string field in manifest.Header)
            {
                values.Header.TryGetValue(field, out string value);
                model.Header.Add(new KeyValuePair<string, string>(field, value ?? string.Empty));
            }

            model.Name = model.Header.Where(h => h.Key == TemplateNames.NameField).Select(h => h.Value).FirstOrDefault();

            if (manifest.GeneratedTypes.Count > 0 && string.IsNullOrWhiteSpace(model.Name))
                problems.Add(new TemplateProblem(file, null, "header." + TemplateNames.NameField + " is empty, and the generated types are named after it."));

            foreach (GeneratedType type in manifest.GeneratedTypes)
                model.Types[type.Id] = type.NameFor(model.Name ?? string.Empty);

            foreach (string key in values.Variables.Keys.Where(k => !manifest.Variables.Any(v => string.Equals(v.Id, k, StringComparison.OrdinalIgnoreCase))))
                problems.Add(new TemplateProblem(file, null, "The values name a variable '" + key + "' it does not have."));

            foreach (TemplateVariable variable in manifest.Variables)
            {
                object value = values.Variables.TryGetValue(variable.Id, out object given) ? given : variable.Default;
                object resolved = Variable(variable, value, file, problems);
                if (resolved != null) model.Values[variable.Id] = resolved;
            }

            if (manifest.TiaVersions.Count > 0)
            {
                TiaVersion version = manifest.TiaVersions.FirstOrDefault(v => v.Version == values.TiaVersion);

                if (version == null)
                    problems.Add(new TemplateProblem(file, null, (string.IsNullOrEmpty(values.TiaVersion)
                                                                     ? "Say which TIA Portal version to render for"
                                                                     : "It has nothing for TIA Portal V" + values.TiaVersion) +
                                                                 ": it lists " + Listed(manifest.TiaVersions.Select(v => v.Version)) + "."));
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

                    if (RangeText.Outside(entries.Count, variable.Min, variable.Max))
                    {
                        problems.Add(new TemplateProblem(file, null, at + " holds " + entries.Count + " entries, outside its own " +
                                                                     RangeText.Of(variable.Min, variable.Max) + "."));
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

                    if (RangeText.Outside(number.Value, variable.Min, variable.Max))
                    {
                        problems.Add(new TemplateProblem(file, null, at + " is " + RangeText.Show(number.Value) + ", outside its own " +
                                                                     RangeText.Of(variable.Min, variable.Max) + "."));
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
        /// A decimal, or a whole number made one. Never a <see cref="double"/>: the manifest keeps
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

        private static string Listed(IEnumerable<string> names) => string.Join(", ", names);
    }
}
