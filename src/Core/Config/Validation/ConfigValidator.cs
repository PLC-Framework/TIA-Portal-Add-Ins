namespace Core.Config.Validation
{
    /// <summary>
    /// The whole document, for whoever needs the whole document - which is the editor,
    /// before it saves.
    ///
    /// **An action should not use this.** Validation is scoped per concern on purpose: the
    /// hierarchy action reads only <c>hierarchy</c> and should refuse to run only over
    /// problems in <c>hierarchy</c>. Running the composite there would make a broken
    /// coding style stop a folder tree that is perfectly fine, and "required" would quietly
    /// become a property of the document rather than of the concern that needs it.
    /// </summary>
    public static class ConfigValidator
    {
        public static ValidationResult Validate(Config config)
        {
            Issues issues = new Issues();

            if (!issues.RequiredObject("", config))
            {
                // Nothing to walk. The empty path reads oddly in a report, so say it plainly.
                return new ValidationResult(new[]
                {
                    new ValidationIssue("(document)", "There is no configuration to validate.")
                });
            }

            MetadataValidator.Collect(config.Metadata, "metadata", issues);

            Repositories(config, issues);

            string projectConfig = "projectConfig";
            if (issues.RequiredObject(projectConfig, config.ProjectConfig))
            {
                HierarchyValidator.Collect(
                    config.ProjectConfig.Hierarchy, Issues.Field(projectConfig, "hierarchy"), issues);

                CodingStyleValidator.Collect(
                    config.ProjectConfig.CodingStyle, Issues.Field(projectConfig, "codingStyle"), issues);
            }

            return new ValidationResult(issues.All);
        }

        /// <summary>
        /// Only the section <c>coreSource</c> selects. The other one is left alone
        /// entirely - it is normal for a project to carry both and use one, and reporting
        /// the unused half would train the reader to ignore the report.
        /// </summary>
        private static void Repositories(Config config, Issues issues)
        {
            string source = MetadataValidator.SourceOf(config.Metadata);

            // Null means no repository - coreSource is null, or not one of the two, which is
            // already reported and should not be complained about twice. Either way both
            // sections are left alone, whatever state they are in.
            if (source == null) return;

            if (source == MetadataValidator.Remote)
            {
                RepositoryValidator.CollectRemote(
                    config.CoreRemoteRepositoryConfig, "coreRemoteRepositoryConfig", issues);
            }
            else
            {
                RepositoryValidator.CollectLocal(
                    config.CoreLocalRepositoryConfig, "coreLocalRepositoryConfig", issues);
            }
        }
    }
}
