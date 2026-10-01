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

            string repositoryField = Issues.Field(path, "repository");
            string folderField = Issues.Field(path, local.FolderKey);     // coreFolder, or its former name
            string graphField = Issues.Field(path, "dependencyFile");
            string templatesField = Issues.Field(path, "templateFolder");

            // **Every ${VAR} first, in every path field, before the disk is asked anything.**
            // Whether a variable is set does not depend on whether a folder exists, so a missing
            // repository must not hide an unset variable below it. Left literal, an unset one
            // came out as "'…\${TEMPLATES}' does not exist" - true and useless - or as nothing
            // where a folder of that literal name happened to exist. Codex's first two reviews
            // found it (2026-10-01): the template folder never asked, then a missing repository
            // stopping the walk before the folders below it were asked.
            bool repositoryUnset = Unresolved(local.Repository, repositoryField, lookup, issues);
            bool folderUnset = Unresolved(local.Folder, folderField, lookup, issues);
            bool graphUnset = Unresolved(local.DependencyFile, graphField, lookup, issues);
            bool templatesUnset = Unresolved(local.TemplateFolder, templatesField, lookup, issues);

            if (repositoryUnset) return;

            string repository = Variables.Expand(local.Repository, lookup);
            if (string.IsNullOrWhiteSpace(repository)) return;

            if (!DirectoryExists(repository))
            {
                issues.Add(repositoryField, "'" + repository + "' does not exist.");
                return;
            }

            // Only worth walking further once the root is there; otherwise every level below
            // reports the same absence again. A field whose variable is unset has been said and
            // is not looked for.
            if (!folderUnset) CoreFiles(repository, local, folderField, graphUnset ? null : graphField, lookup, issues);
            if (!templatesUnset) TemplateFolder(repository, local, templatesField, lookup, issues);
        }

        /// <param name="graphField">Null when the dependency file's variable is unset - already said.</param>
        private static void CoreFiles(
            string repository, CoreLocalRepositoryConfig local, string folderField, string graphField,
            Func<string, string> lookup, Issues issues)
        {
            if (string.IsNullOrWhiteSpace(local.Folder)) return;

            string folder = Beneath(repository, local.Folder, folderField, lookup, issues);
            if (folder == null) return;

            if (!DirectoryExists(folder))
            {
                issues.Add(folderField, "'" + folder + "' does not exist.");
                return;
            }

            if (graphField == null || string.IsNullOrWhiteSpace(local.DependencyFile)) return;

            string dependencies = Beneath(folder, local.DependencyFile, graphField, lookup, issues);

            if (dependencies != null && !FileExists(dependencies))
                issues.Add(graphField, "'" + dependencies + "' does not exist.");
        }

        /// <summary>
        /// Optional, so blank is nothing to report: this repository offers no templates.
        ///
        /// **Checked on its own rather than after the core folder**, because it hangs off the
        /// repository root and not off the core: a core folder that is missing says nothing
        /// about whether the templates are there, and stopping at it would hide the one problem
        /// behind the other.
        /// </summary>
        private static void TemplateFolder(
            string repository, CoreLocalRepositoryConfig local, string field,
            Func<string, string> lookup, Issues issues)
        {
            if (string.IsNullOrWhiteSpace(local.TemplateFolder)) return;

            string templates = Beneath(repository, local.TemplateFolder, field, lookup, issues);

            if (templates != null && !DirectoryExists(templates))
                issues.Add(field, "'" + templates + "' does not exist.");
        }

        /// <summary>
        /// A path from the file joined onto one that exists, or null when it cannot be one -
        /// reported against the field it came from. Its variables have already been asked about
        /// by the caller, so only set ones arrive here.
        ///
        /// **`Path.Combine` throws on a character Windows refuses**, where `Directory.Exists`
        /// quietly answers false, and the editor runs this pass on every keystroke with nothing
        /// around it. Measured on 2026-09-30, adding the template folder: one `|` typed into the
        /// core's folder box ended the config editor, *"Illegal characters in path"* out of
        /// `Path.Combine` in its log as the last line it wrote.
        /// </summary>
        private static string Beneath(
            string root, string relative, string field, Func<string, string> lookup, Issues issues)
        {
            string expanded = Variables.Expand(relative, lookup);

            try
            {
                // The file writes forward slashes, as a node's `file` does; the message names
                // a Windows path, as `LocalSource` resolves one, rather than half of each.
                return Path.Combine(root, expanded.Replace('/', Path.DirectorySeparatorChar));
            }
            catch (Exception)
            {
                issues.Add(field, "'" + expanded + "' is not a path Windows accepts.");
                return null;
            }
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
