using System;

namespace Core.Config.Validation
{
    /// <summary>
    /// The two repository sections. Only the one <c>coreSource</c> names is ever checked;
    /// the other may be absent, stale or half-filled and none of it matters.
    /// </summary>
    public static class RepositoryValidator
    {
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

        private static bool IsAbsoluteUrl(string value)
        {
            Uri parsed;
            return Uri.TryCreate(value, UriKind.Absolute, out parsed) &&
                   (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps);
        }
    }
}
