using System;
using System.Collections.Generic;
using System.Linq;

using BlockTemplate.Manifest;

namespace BlockTemplate.Rendering
{
    /// <summary>
    /// What a template is rendered with: the TIA Portal version it is for, the header's fields and
    /// the variables' values - what the form will fill in. <see cref="Defaults"/> is where it starts.
    ///
    /// **Keys are matched whatever their case**, and reach the template spelled as the manifest
    /// spells them: the manifest already refuses two names differing only in case, so one key can
    /// only ever mean one name.
    /// </summary>
    public sealed class TemplateValues
    {
        public TemplateValues(string tiaVersion)
        {
            TiaVersion = tiaVersion;
        }

        /// <summary>The TIA Portal major version rendered for, <c>"20"</c> or <c>"21"</c>: which of <c>tiaVersions</c> applies.</summary>
        public string TiaVersion { get; }

        /// <summary>The header's fields, each a text; absent and null are both empty.</summary>
        public IDictionary<string, string> Header { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The variables' values: a list of strings, a <see cref="bool"/>, a <see cref="decimal"/>
        /// (or a whole number) or a string, each as its type says. Absent is the variable's default.
        /// </summary>
        public IDictionary<string, object> Variables { get; } = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// What a form starts from: <c>version</c> at 0.1, every other header field empty - the
        /// author always typed, since the VM's own user is <c>vm</c> - and every variable at its
        /// default.
        /// </summary>
        public static TemplateValues Defaults(TemplateManifest manifest, string tiaVersion)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));

            TemplateValues values = new TemplateValues(tiaVersion);

            foreach (string field in manifest.Header)
                values.Header[field] = string.Equals(field, TemplateNames.VersionField, StringComparison.OrdinalIgnoreCase)
                    ? TemplateNames.FirstVersion
                    : string.Empty;

            foreach (TemplateVariable variable in manifest.Variables)
                values.Variables[variable.Id] = variable.Default is IEnumerable<string> list && !(variable.Default is string)
                    ? list.ToList()
                    : variable.Default;

            return values;
        }
    }
}
