using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;

namespace Core.Repo.Remote
{
    /// <summary>
    /// Where the copy in <c>repo\core\</c> came from, written beside it after a remote core
    /// has been brought down whole: <c>repo\core.origin.json</c>.
    ///
    /// **It is a record, not a shortcut**, and that was a decision rather than an oversight.
    /// The obvious use is to skip the download when the branch has not moved - one request
    /// instead of two and a listing. It is not taken, because the listing is what repairs a
    /// copy somebody edited, truncated or half-deleted: the file hashes are compared against
    /// the repository's on every run, and trusting a commit marker instead would hand a
    /// comparison a copy nobody has checked. Two requests and a few hundred hashes are
    /// cheaper than that.
    ///
    /// **What it is for is saying which core a result was against.** Until now nothing could:
    /// a local core is named by a folder that has moved on since, and a remote one by a branch
    /// that has. A commit is the one answer that stays true.
    ///
    /// **Only written when the copy is whole.** A run that could not fetch every file leaves
    /// the previous marker alone rather than claiming a commit the folder does not hold.
    /// </summary>
    [DataContract(Name = "origin", Namespace = "")]
    public sealed class CoreOrigin
    {
        /// <summary>The file, beside the copy it describes.</summary>
        public const string FileName = "core.origin.json";

        [DataMember(Name = "owner", Order = 1)]
        public string Owner { get; set; }

        [DataMember(Name = "repository", Order = 2)]
        public string Repository { get; set; }

        [DataMember(Name = "branch", Order = 3)]
        public string Branch { get; set; }

        [DataMember(Name = "folder", Order = 4)]
        public string Folder { get; set; }

        /// <summary>The commit the copy holds.</summary>
        [DataMember(Name = "commit", Order = 5)]
        public string Commit { get; set; }

        /// <summary>When it was brought down, in UTC.</summary>
        [DataMember(Name = "when", Order = 6)]
        public string When { get; set; }

        [DataMember(Name = "files", Order = 7)]
        public int Files { get; set; }

        /// <summary>The first seven characters, which is how a commit is read out loud.</summary>
        public string ShortCommit =>
            string.IsNullOrEmpty(Commit) ? string.Empty : (Commit.Length <= 7 ? Commit : Commit.Substring(0, 7));

        /// <summary>What a caption says: <c>owner/repo@branch, commit abc1234</c>.</summary>
        public override string ToString() =>
            Owner + "/" + Repository + "@" + Branch + ", commit " + ShortCommit;

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
                // A marker that will not read says nothing about the copy beside it, which is
                // checked file by file anyway.
                return null;
            }
        }

        /// <summary>The marker for a mirror that has just finished.</summary>
        public static CoreOrigin Of(Config.CoreRemoteRepositoryConfig repository, RemoteCopyResult copied, int files) =>
            new CoreOrigin
            {
                Owner = repository?.Owner,
                Repository = repository?.Repository,
                Branch = repository?.Branch,
                Folder = repository?.Folder,
                Commit = copied?.Commit,
                When = DateTime.UtcNow.ToString("o"),
                Files = files
            };
    }
}
