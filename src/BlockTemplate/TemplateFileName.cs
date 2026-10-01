using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

using Core.Imports;

namespace BlockTemplate
{
    /// <summary>
    /// What a template's file name says: <c>&lt;base&gt;.v&lt;major&gt;.json</c> for its manifest,
    /// <c>&lt;base&gt;.v&lt;major&gt;.&lt;extension&gt;</c> for the file it renders, and
    /// <c>&lt;base&gt;.&lt;id&gt;.v&lt;major&gt;.&lt;extension&gt;</c> for one of its sub-templates -
    /// <c>_oc_conveyor.v1.json</c>, <c>_oc_conveyor.v1.xml</c>, <c>_oc_conveyor.settings.v1.udt</c>.
    ///
    /// **The file name is the version, and only the major** (the maintainer's rule): every edition
    /// of a template breaks the one before, so there is no minor to tell apart. **The extension is
    /// the format**, exactly as for an import - which is why the set of extensions is
    /// <see cref="ImportFiles"/>' rather than a second list kept here - with <c>.json</c> beside them
    /// for the one file that is not rendered.
    ///
    /// **Neither the base nor the id may hold a dot**, or the name could be read two ways. Note what
    /// that means for a name carrying a TIA version - <c>_oc_seq000.v20.xml</c> reads as major 20:
    /// what differs between TIA versions belongs in the manifest's <c>tiaVersions</c>, not in a
    /// file name.
    /// </summary>
    public sealed class TemplateFileName
    {
        /// <summary>
        /// The manifest's own file, beside the one it describes (the maintainer's choice,
        /// 2026-10-01). It sat in the template's first comment until TIA refused a <c>.s7dcl</c>
        /// carrying a <c>(* *)</c>: a file of its own asks nothing of any format.
        /// </summary>
        public const string ManifestExtension = ".json";

        private static readonly Regex Shape = new Regex(
            @"^(?<base>[^.]+)(?:\.(?<part>[^.]+))?\.[vV](?<major>[0-9]+)(?<extension>\.[^.]+)$",
            RegexOptions.CultureInvariant);

        private TemplateFileName(string @base, string part, int major, string extension)
        {
            Base = @base;
            Part = part;
            Major = major;
            Extension = extension;
        }

        public string Base { get; }

        /// <summary>The sub-template's id, or null for the template's own files.</summary>
        public string Part { get; }

        public int Major { get; }

        /// <summary>Lower case, with its dot: <c>.json</c>, <c>.xml</c>, <c>.s7dcl</c>.</summary>
        public string Extension { get; }

        /// <summary>The template's own - its manifest or the file it renders - not a sub-template's.</summary>
        public bool IsMain => Part == null;

        /// <summary>A manifest: what a template is read through, and never rendered.</summary>
        public bool IsManifest => Extension == ManifestExtension;

        /// <summary>A <c>.s7res</c>: never a template on its own, only the second half of a <c>.s7dcl</c>.</summary>
        public bool IsResource => Extension == ImportFiles.ResourceExtension;

        /// <summary>The template's manifest file name: <c>&lt;base&gt;.v&lt;major&gt;.json</c>.</summary>
        public string ManifestName => Base + ".v" + Major + ManifestExtension;

        /// <summary>
        /// What <paramref name="fileName"/> says, or null with <paramref name="problem"/> saying why
        /// it is not a template's name.
        /// </summary>
        public static TemplateFileName Parse(string fileName, out string problem)
        {
            problem = null;
            Match match = Shape.Match(fileName ?? string.Empty);

            if (!match.Success)
            {
                problem = "'" + fileName + "' is not named as a template is: <base>.v<major>.json, the file it renders " +
                          "beside it with its own extension, and <base>.<id>.v<major>.<extension> for a sub-template - " +
                          "with no other dot.";
                return null;
            }

            string extension = match.Groups["extension"].Value.ToLowerInvariant();

            if (extension != ManifestExtension && !ImportFiles.Extensions.Contains(extension))
            {
                problem = "'" + fileName + "' ends in " + extension + ", which is neither a template's " + ManifestExtension +
                          " nor a format TIA Portal takes in: " + string.Join(", ", ImportFiles.Extensions) + ".";
                return null;
            }

            if (!int.TryParse(match.Groups["major"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int major))
            {
                problem = "'" + fileName + "' carries a major version too large to be one.";
                return null;
            }

            string part = match.Groups["part"].Success ? match.Groups["part"].Value : null;
            return new TemplateFileName(match.Groups["base"].Value, part, major, extension);
        }
    }
}
