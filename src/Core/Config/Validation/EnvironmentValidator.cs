using System;
using System.IO;

using Core.Secrets;

namespace Core.Config.Validation
{
    /// <summary>
    /// The checks that depend on the machine rather than on the file: a path that exists,
    /// a <c>${VAR}</c> that resolves.
    ///
    /// **Kept apart from the structural validator on purpose, and the distinction is not
    /// pedantry.** A configuration is not wrong because it is being read on a station where
    /// a drive is not mapped or the token has not been entered yet — it is *unusable here,
    /// now*, which is a different sentence and often a temporary one. The structural pass
    /// is pure and safe to run inside TIA Portal; this one touches disk, so it runs when
    /// somebody asks rather than on every load.
    ///
    /// What it deliberately does **not** do is reach the network. Whether GitHub answers is
    /// a question with a timeout attached, and a validator that can hang for thirty seconds
    /// is one nobody runs.
    /// </summary>
    public static class EnvironmentValidator
    {
        /// <param name="secrets">
        /// Where <c>${VAR}</c> is resolved from. Defaults to the user's .env with the
        /// process environment behind it; a caller passes its own to test without a file.
        /// </param>
        public static ValidationResult Validate(Config config, Func<string, string> secrets = null)
        {
            Issues issues = new Issues();
            Func<string, string> lookup = secrets ?? DotEnv.Lookup;

            if (config == null) return ValidationResult.Valid;

            string source = MetadataValidator.SourceOf(config.Metadata);

            // Neither section is checked when coreSource names none: a configuration with no
            // repository has no path to find, and one naming something outside the set has
            // already been reported by the structural pass.
            if (source == MetadataValidator.Remote)
                Remote(config.CoreRemoteRepositoryConfig, "coreRemoteRepositoryConfig", lookup, issues);
            else if (source == MetadataValidator.Local)
                Local(config.CoreLocalRepositoryConfig, "coreLocalRepositoryConfig", lookup, issues);

            return new ValidationResult(issues.All);
        }

        private static void Remote(
            CoreRemoteRepositoryConfig remote, string path, Func<string, string> lookup, Issues issues)
        {
            if (remote == null) return;

            // An absent token is a public repository, which is legitimate. A token that
            // names a variable nobody has set is the failure worth catching: it looks
            // configured and produces an unexplained 401 much later.
            if (string.IsNullOrWhiteSpace(remote.Token)) return;

            Unresolved(remote.Token, Issues.Field(path, "token"), lookup, issues);
        }

        private static void Local(
            CoreLocalRepositoryConfig local, string path, Func<string, string> lookup, Issues issues)
        {
            if (local == null) return;

            string repository = Variables.Expand(local.Repository, lookup);

            if (Unresolved(local.Repository, Issues.Field(path, "repository"), lookup, issues)) return;
            if (string.IsNullOrWhiteSpace(repository)) return;

            if (!DirectoryExists(repository))
            {
                issues.Add(Issues.Field(path, "repository"), "'" + repository + "' does not exist.");
                return;
            }

            // Only worth walking further once the root is there; otherwise every level
            // below reports the same absence again.
            if (string.IsNullOrWhiteSpace(local.Folder)) return;

            string folder = Path.Combine(repository, Variables.Expand(local.Folder, lookup));

            if (!DirectoryExists(folder))
            {
                issues.Add(Issues.Field(path, "folder"), "'" + folder + "' does not exist.");
                return;
            }

            if (string.IsNullOrWhiteSpace(local.DependencyFile)) return;

            string dependencies = Path.Combine(folder, Variables.Expand(local.DependencyFile, lookup));

            if (!FileExists(dependencies))
                issues.Add(Issues.Field(path, "dependencyFile"), "'" + dependencies + "' does not exist.");
        }

        /// <summary>
        /// Reports any <c>${VAR}</c> the lookup cannot answer.
        /// </summary>
        /// <returns>True when something was left unresolved, so the caller can stop.</returns>
        private static bool Unresolved(
            string value, string path, Func<string, string> lookup, Issues issues)
        {
            bool any = false;

            foreach (string name in Variables.References(value))
            {
                if (!string.IsNullOrEmpty(Safe(lookup, name))) continue;

                issues.Add(path,
                    "${" + name + "} is not set. Add it to the .env at " +
                    (InstallPaths.EnvFile ?? "the per-user folder") + ".");

                any = true;
            }

            return any;
        }

        private static string Safe(Func<string, string> lookup, string name)
        {
            try { return lookup(name); }
            catch (Exception) { return null; }
        }

        // A path on a disconnected share throws rather than returning false, and a
        // validator that reports "does not exist" beats one that takes the caller down.
        private static bool DirectoryExists(string path)
        {
            try { return Directory.Exists(path); }
            catch (Exception) { return false; }
        }

        private static bool FileExists(string path)
        {
            try { return File.Exists(path); }
            catch (Exception) { return false; }
        }
    }
}
