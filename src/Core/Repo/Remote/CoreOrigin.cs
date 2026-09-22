using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;

namespace Core.Repo.Remote
{
    /// <summary>
    /// Where the project's <c>repo\core.json</c> came from, written beside it after a remote
    /// core's graph has been brought down: <c>repo\core.origin.json</c>.
    ///
    /// **It is a record, not a shortcut**, and that was a decision rather than an oversight.
    /// The obvious use is to skip the listing when the branch has not moved - one request
    /// instead of two. It is not taken, because the listing is what repairs a copy somebody
    /// edited or truncated: the graph's hash is compared against the repository's on every
    /// Load, and trusting a commit marker instead would hand a comparison a copy nobody has
    /// checked. One request is cheaper than that.
    ///
    /// **What it is for is saying which core a result was against.** Until now nothing could:
    /// a local core is named by a folder that has moved on since, and a remote one by a branch
    /// that has. A commit is the one answer that stays true.
    ///
    /// **Only written when the graph came down whole.** A Load that could not fetch it leaves
    /// the previous marker alone rather than claiming a commit the copy does not hold.
    ///
    /// **It no longer counts files** (2026-09-22): it recorded how many the mirror held, and
    /// there is no mirror - the graph comes down alone and a source only when an import reads
    /// it. A marker written before still reads; the count is simply ignored.
    ///
    /// **It records the host as well as the path inside it**, because owner, repository and
    /// branch are not an answer on their own: <c>acme/code@main</c> on GitHub and the same on
    /// GitLab are two different cores, and a marker that could not tell them apart would be
    /// exactly the half-truth it exists to close.
    /// </summary>
    [DataContract(Name = "origin", Namespace = "")]
    public sealed class CoreOrigin
    {
        /// <summary>The file, beside the copy it describes.</summary>
        public const string FileName = "core.origin.json";

        /// <summary>Whose API it was read with - <c>github</c> today.</summary>
        [DataMember(Name = "provider", Order = 1)]
        public string Provider { get; set; }

        /// <summary>
        /// The endpoint it was read from, as the configuration spelled it. **Kept verbatim**
        /// rather than reduced to a host, so a self-hosted instance behind a path is still
        /// named in full by the file even where a caption only has room for the host.
        /// </summary>
        [DataMember(Name = "apiUrl", Order = 2)]
        public string ApiUrl { get; set; }

        [DataMember(Name = "owner", Order = 3)]
        public string Owner { get; set; }

        [DataMember(Name = "repository", Order = 4)]
        public string Repository { get; set; }

        [DataMember(Name = "branch", Order = 5)]
        public string Branch { get; set; }

        [DataMember(Name = "folder", Order = 6)]
        public string Folder { get; set; }

        /// <summary>The commit the copy holds.</summary>
        [DataMember(Name = "commit", Order = 7)]
        public string Commit { get; set; }

        /// <summary>When it was brought down, in UTC.</summary>
        [DataMember(Name = "when", Order = 8)]
        public string When { get; set; }

        /// <summary>The first seven characters, which is how a commit is read out loud.</summary>
        public string ShortCommit =>
            string.IsNullOrEmpty(Commit) ? string.Empty : (Commit.Length <= 7 ? Commit : Commit.Substring(0, 7));

        /// <summary>
        /// What a line says it was read from: the endpoint's host, the provider's name when
        /// there is no endpoint, and nothing at all when a marker written before this key
        /// existed is read back - where saying nothing is the only honest answer.
        /// </summary>
        public string Host
        {
            get
            {
                if (string.IsNullOrWhiteSpace(ApiUrl)) return Provider ?? string.Empty;

                Uri parsed;

                return Uri.TryCreate(ApiUrl.Trim(), UriKind.Absolute, out parsed)
                    ? parsed.Host
                    : ApiUrl.Trim();
            }
        }

        /// <summary>
        /// What a caption says: <c>owner/repo@branch on api.github.com, commit abc1234</c>.
        /// </summary>
        public override string ToString()
        {
            string where = Host;

            return Owner + "/" + Repository + "@" + Branch +
                   (where.Length == 0 ? string.Empty : " on " + where) +
                   ", commit " + ShortCommit;
        }

        public static string PathFor(string projectDirectory)
        {
            string repo = RepoPaths.For(projectDirectory);

            return repo == null ? null : Path.Combine(repo, FileName);
        }

        /// <summary>
        /// Writes the marker. **Never fails a run**: what it records is worth having and
        /// nothing depends on it, so a folder that will not take it costs a caption rather
        /// than a core.
        /// </summary>
        public static void Write(CoreOrigin origin, string projectDirectory)
        {
            string path = PathFor(projectDirectory);

            if (origin == null || path == null) return;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));

                using (FileStream stream = File.Create(path))
                using (XmlDictionaryWriter writer =
                       JsonReaderWriterFactory.CreateJsonWriter(stream, Encoding.UTF8, false, true, "  "))
                {
                    new DataContractJsonSerializer(typeof(CoreOrigin)).WriteObject(writer, origin);
                    writer.Flush();
                }
            }
            catch (Exception)
            {
                // See above: a record, not a dependency.
            }
        }

        /// <summary>What the last remote refresh wrote, or null when there is none to read.</summary>
        public static CoreOrigin Read(string projectDirectory)
        {
            string path = PathFor(projectDirectory);

            try
            {
                if (path == null || !File.Exists(path)) return null;

                using (FileStream stream = File.OpenRead(path))
                    return new DataContractJsonSerializer(typeof(CoreOrigin)).ReadObject(stream) as CoreOrigin;
            }
            catch (Exception)
            {
                // A marker that will not read says nothing about the graph beside it, which is
                // checked against the repository on every Load anyway.
                return null;
            }
        }

        /// <summary>The marker for a graph that has just come down.</summary>
        public static CoreOrigin Of(Config.CoreRemoteRepositoryConfig repository, RemoteCopyResult copied) =>
            new CoreOrigin
            {
                Provider = Config.Validation.RepositoryValidator.ProviderOf(repository),
                ApiUrl = repository?.ApiUrl,
                Owner = repository?.Owner,
                Repository = repository?.Repository,
                Branch = repository?.Branch,
                Folder = repository?.Folder,
                Commit = copied?.Commit,
                When = DateTime.UtcNow.ToString("o")
            };
    }
}
