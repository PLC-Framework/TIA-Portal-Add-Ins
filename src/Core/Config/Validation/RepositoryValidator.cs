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

        public static ValidationResult ValidateRemote(
            CoreRemoteRepositoryConfig remote, string path = "coreRemoteRepositoryConfig")
        {
            Issues issues = new Issues();
            CollectRemote(remote, path, issues);
            return new ValidationResult(issues.All);
        }

        public static ValidationResult ValidateLocal(
            CoreLocalRepositoryConfig local, string path = "coreLocalRepositoryConfig")
        {
            Issues issues = new Issues();
            CollectLocal(local, path, issues);
            return new ValidationResult(issues.All);
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
            // repository, and a file carrying a literal token instead of ${GITHUB_TOKEN}
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
