using System;
using System.IO;
using System.Text;

namespace Satellite.ConfigEditor.Document
{
    /// <summary>
    /// The <c>.gitignore</c> that goes inside a TIA project's <c>.plc-framework\</c>.
    ///
    /// **It ignores everything and re-admits what must be versioned, rather than listing
    /// what to skip.** A deny-list only knows the folders that exist today — <c>exports\</c>,
    /// <c>logs\</c>, <c>tmp\</c>, <c>reports\</c>, named in <see cref="Core.Config.ConfigPaths"/>.
    /// The next generated thing the framework writes would be committed by default, and
    /// nobody would notice until it was already in the history — which is the wrong way round
    /// for a folder whose whole purpose is generated output.
    ///
    /// The stake is higher than tidiness. <c>exports\</c> holds workbooks of values read out
    /// of a live CPU: plant configuration, sitting in a folder next to <c>.version-control\</c>.
    /// Two lines of allow-list are a cheap way never to have that conversation.
    ///
    /// **config.schema.json is re-admitted deliberately, and it is not optional.**
    /// config.json carries <c>"$schema": "./config.schema.json"</c>, so a clone without it
    /// points at a file that is not there and the editor's validation stops — silently,
    /// which is the exact failure that ruled out referencing the schema by URL.
    ///
    /// **Written only when absent**, unlike the schema beside it. A team may add a line for
    /// their own tooling, and rewriting this on every save would be the editor overruling
    /// them. The schema is generated and therefore ours to replace; this is a starting
    /// point and therefore theirs.
    /// </summary>
    public static class ProjectGitIgnore
    {
        public const string FileName = ".gitignore";

        /// <summary>
        /// Short enough to read at a glance, which is the point: somebody has to be able to
        /// see why their file is not being committed. Kept as a literal rather than an
        /// embedded resource - there is no drift risk, because an allow-list names only what
        /// stays, so a new generated folder needs no change here at all.
        /// </summary>
        private const string Content =
            "# Everything the framework writes into this folder is generated, and some of it\n" +
            "# - the workbooks under exports\\ - holds values read out of a live CPU. So the\n" +
            "# rule is the other way round from usual: ignore all of it, and name what stays.\n" +
            "#\n" +
            "# Adding a generated folder later needs no change here.\n" +
            "*\n" +
            "\n" +
            "!.gitignore\n" +
            "!config.json\n" +
            "\n" +
            "# Not optional: config.json points at this by relative path, so a clone without\n" +
            "# it validates nothing, and says nothing about why.\n" +
            "!config.schema.json\n";

        /// <summary>
        /// Puts a .gitignore beside config.json if there is not one already.
        /// </summary>
        /// <returns>A sentence worth showing, or null when everything was ordinary.</returns>
        public static string Ensure(string configPath)
        {
            if (string.IsNullOrWhiteSpace(configPath)) return null;

            try
            {
                string folder = Path.GetDirectoryName(configPath);
                string target = Path.Combine(folder ?? string.Empty, FileName);

                if (File.Exists(target)) return null;

                Directory.CreateDirectory(folder);

                // The literal above uses \n; Git does not care, and neither does any editor
                // that opens this. Writing it verbatim keeps the file identical everywhere.
                File.WriteAllText(target, Content, new UTF8Encoding(false));

                return null;
            }
            catch (Exception exception)
            {
                // Never a reason to fail a save: the configuration is written either way, and
                // what is lost is a default somebody can add by hand.
                return FileName + " could not be written beside it: " + exception.Message;
            }
        }
    }
}
