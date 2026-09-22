using System.Text.RegularExpressions;

namespace Core.Config.Validation
{
    /// <summary>
    /// The <c>metadata</c> section.
    ///
    /// Small, but it is the section that decides the rest of the file: <c>coreSource</c>
    /// selects which of the two repository sections is required, and the other one is not
    /// validated at all.
    ///
    /// **<c>coreSource</c> may be null, and null means no repository.** A core is needed
    /// only by the actions that read one, and a project that runs none of them should not
    /// have to invent a path to satisfy a validator. Absent and null are the same answer:
    /// the model cannot tell them apart - the serializer hands both over as null - and a
    /// schema demanding the key while Core could not would be two validators disagreeing.
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

            CollectSource(metadata, path, issues);

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
        /// <c>coreSource</c> alone, for <see cref="RepositoryValidator.Validate"/>: which core a
        /// project names is that action's concern, and a mistyped <c>version</c> beside it is not.
        /// </summary>
        internal static void CollectSource(Metadata metadata, string path, Issues issues)
        {
            // Present means one of the two. An empty string is not null: it is a value outside
            // the set, and reading it as "no repository" would turn a half-typed word into a
            // decision nobody made.
            if (metadata.CoreSource != null &&
                !string.Equals(metadata.CoreSource, Local, System.StringComparison.Ordinal) &&
                !string.Equals(metadata.CoreSource, Remote, System.StringComparison.Ordinal))
            {
                issues.Add(Issues.Field(path, "coreSource"),
                    "Must be " + Local + ", " + Remote + ", or null for no repository.");
            }
        }

        /// <summary>
        /// Which repository section this configuration must carry. Null when it needs none:
        /// <c>coreSource</c> is null, or it is not one of the two - which is already
        /// reported, and demanding a section on top of it would be a second complaint.
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
