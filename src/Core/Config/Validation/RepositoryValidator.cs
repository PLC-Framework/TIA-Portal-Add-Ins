using System;

namespace Core.Config.Validation
{
    /// <summary>
    /// The two repository sections. Only the one <c>coreSource</c> names is ever checked;
    /// the other may be absent, stale or half-filled and none of it matters.
    /// </summary>
    public static class RepositoryValidator
    {
        /// <summary>The one remote provider this framework can speak, and the default.</summary>
        public const string GitHub = "github";

        /// <summary>
        /// The repository concern as an action reads it: <c>metadata.coreSource</c>, and the one
        /// section it selects. What reading a core may refuse over, and nothing else - the rest
        /// of <c>metadata</c> is not this concern's, so a mistyped <c>version</c> does not stop a
        /// comparison.
        ///
        /// **It had two entry points until 2026-09-22, one per section, and nothing called
        /// either**: `PlcCoreRefresh` checked `coreSource` by hand, `LocalSource` checked the
        /// local fields by hand, and an empty remote field was caught three layers down in three
        /// wordings - one of them *"An owner is required. Parameter name: owner"*, which names no
        /// field of `config.json`. The same shape as the hierarchy's validator, which had no
        /// caller either until that morning.
        /// </summary>
        public static ValidationResult Validate(Config config)
        {
            Issues issues = new Issues();

            if (issues.RequiredObject("metadata", config?.Metadata))
            {
                MetadataValidator.CollectSource(config.Metadata, "metadata", issues);
                CollectSelected(config, issues);
            }

            return new ValidationResult(issues.All);
        }

        /// <summary>
        /// Only the section <c>coreSource</c> selects. The other one is left alone entirely - it
        /// is normal for a project to carry both and use one, and reporting the unused half would
        /// train the reader to ignore the report. Shared with <see cref="ConfigValidator"/>, so
        /// the document and the action cannot disagree about which section counts.
        /// </summary>
        internal static void CollectSelected(Config config, Issues issues)
        {
            string source = MetadataValidator.SourceOf(config.Metadata);

            // Null means no repository - coreSource is null, or not one of the two, which is
            // already reported and should not be complained about twice. Either way both
            // sections are left alone, whatever state they are in.
            if (source == null) return;

            if (source == MetadataValidator.Remote)
                CollectRemote(config.CoreRemoteRepositoryConfig, "coreRemoteRepositoryConfig", issues);
            else
                CollectLocal(config.CoreLocalRepositoryConfig, "coreLocalRepositoryConfig", issues);
        }

        internal static void CollectRemote(
            CoreRemoteRepositoryConfig remote, string path, Issues issues)
        {
            if (!issues.RequiredObject(path, remote)) return;

            // Absent is github, so no key is not a problem. A key with something else in it is:
            // an empty string is not null here either, for the reason coreSource records - a
            // half-typed word must not be read as a decision nobody made.
            if (remote.Provider != null && !Knows(ProviderOf(remote)))
                issues.Add(Issues.Field(path, "provider"),
                    "Must be " + GitHub + ", or absent for " + GitHub + ".");

            string apiUrl = Issues.Field(path, "apiUrl");
            if (issues.Required(apiUrl, remote.ApiUrl) && !IsAbsoluteUrl(remote.ApiUrl))
                issues.Add(apiUrl, "Must be an absolute URL, such as https://api.github.com/.");

            issues.Required(Issues.Field(path, "owner"), remote.Owner);
            issues.Required(Issues.Field(path, "repository"), remote.Repository);
            issues.Required(Issues.Field(path, "branch"), remote.Branch);
            issues.Required(Issues.Field(path, "folder"), remote.Folder);
            issues.Required(Issues.Field(path, "dependencyFile"), remote.DependencyFile);

            // token is optional and intentionally unchecked here. Empty means a public
            // repository, and a file carrying a literal token instead of ${REPO_TOKEN}
            // still works - it is a secret in the wrong place, which is the editor's
            // warning to give, not a reason for the Add-In to refuse the file.
        }

        internal static void CollectLocal(
            CoreLocalRepositoryConfig local, string path, Issues issues)
        {
            if (!issues.RequiredObject(path, local)) return;

            // That the path exists is environmental, and checked in the other pass: a
            // configuration written on one station is not wrong because it is being read
            // on another where the drive is not mapped.
            issues.Required(Issues.Field(path, "repository"), local.Repository);
            issues.Required(Issues.Field(path, "folder"), local.Folder);
            issues.Required(Issues.Field(path, "dependencyFile"), local.DependencyFile);
        }

        /// <summary>
        /// Which API dialect a remote section asks for, trimmed - <see cref="GitHub"/> when the
        /// key is absent, and whatever it says when it is there. **Never null**, so a caller can
        /// name it in a sentence, and never silently corrected: an empty string comes back empty
        /// and is refused by <see cref="Knows"/> rather than read as the default.
        /// </summary>
        public static string ProviderOf(CoreRemoteRepositoryConfig remote) =>
            remote?.Provider == null ? GitHub : remote.Provider.Trim();

        /// <summary>
        /// Whether this framework can speak it. **Ordinal**, like every other closed set here:
        /// <c>GitHub</c> is refused exactly as <c>Local</c> is on <c>coreSource</c>, and the
        /// message says what to write instead.
        /// </summary>
        public static bool Knows(string provider) =>
            string.Equals(provider, GitHub, StringComparison.Ordinal);

        private static bool IsAbsoluteUrl(string value)
        {
            Uri parsed;
            return Uri.TryCreate(value, UriKind.Absolute, out parsed) &&
                   (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps);
        }
    }
}
