using System.Text.RegularExpressions;

namespace Core.Config.Validation
{
    /// <summary>
    /// The <c>metadata</c> section.
    ///
    /// Small, but it is the section that decides the rest of the file: <c>coreSource</c>
    /// selects which of the two repository sections is required, and the other one is not
    /// validated at all.
    /// </summary>
    public static class MetadataValidator
    {
        public const string Local = "local";
        public const string Remote = "remote";

        /// <summary>
        /// <c>v</c> followed by two to four components of up to two digits: v2.0, v1.12.3,
        /// v9.9.9.9. Anchored, so trailing rubbish is caught rather than ignored.
        /// </summary>
        private static readonly Regex VersionPattern =
            new Regex(@"^v\d{1,2}(\.\d{1,2}){1,3}$", RegexOptions.CultureInvariant);

        public static ValidationResult Validate(Metadata metadata, string path = "metadata")
        {
            Issues issues = new Issues();
            Collect(metadata, path, issues);
            return new ValidationResult(issues.All);
        }

        internal static void Collect(Metadata metadata, string path, Issues issues)
        {
            if (!issues.RequiredObject(path, metadata)) return;

            string coreSource = Issues.Field(path, "coreSource");
            if (issues.Required(coreSource, metadata.CoreSource))
                issues.OneOf(coreSource, metadata.CoreSource, Local, Remote);

            // Optional, but a version that is present and unreadable is worse than none:
            // it looks like it means something.
            if (!string.IsNullOrWhiteSpace(metadata.Version) &&
                !VersionPattern.IsMatch(metadata.Version))
            {
                issues.Add(Issues.Field(path, "version"),
                    "Must look like v2.0, v1.12.3 or v9.9.9.9 - 'v' and two to four " +
                    "numbers of at most two digits.");
            }

            // author and description are informative and deliberately unchecked.
        }

        /// <summary>
        /// Which repository section this configuration must carry. Null when
        /// <c>coreSource</c> is missing or not one of the two, in which case that is
        /// already reported and neither section should be demanded on top of it.
        /// </summary>
        internal static string SourceOf(Metadata metadata)
        {
            string value = metadata?.CoreSource;

            if (string.Equals(value, Local, System.StringComparison.Ordinal)) return Local;
            if (string.Equals(value, Remote, System.StringComparison.Ordinal)) return Remote;

            return null;
        }
    }
}
