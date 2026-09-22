using System;
using System.Collections.Generic;

using Core.Config;

namespace Core.Repo.Remote
{
    /// <summary>
    /// How a core that lives in a repository somebody else hosts is reached.
    ///
    /// **A port, because `Core` may not hold the client.** Whatever talks to GitHub pulls
    /// `System.Net.Http`, and `Core` is loaded inside TIA Portal's process - the same reason
    /// `S7PlcWebserverApi` is a project of its own and never a folder in here. So this layer
    /// says what it needs and the window supplies it.
    ///
    /// **It is opened from the configuration rather than handed a built client**, so nothing
    /// outside reads `coreRemoteRepositoryConfig` twice: `PlcCoreRefresh` already has it, and
    /// resolving `${GITHUB_TOKEN}` out of the per-user `.env` is `Core.Secrets`' job.
    /// </summary>
    public interface IRemoteCore
    {
        /// <param name="token">
        /// The token, already resolved - or null, which is how a public repository is read.
        /// </param>
        IRemoteFiles Open(CoreRemoteRepositoryConfig repository, string token);
    }

    /// <summary>
    /// One repository, open: which commit it is at, what is in it, and one file's bytes.
    ///
    /// **Three questions in that order, and the first two are what make the third rare.** A
    /// core is two hundred and fifty files; asking for all of them every time is how a
    /// comparison comes to cost minutes. The commit says whether anything moved at all, and
    /// a file's own hash says whether *that* one did.
    /// </summary>
    public interface IRemoteFiles : IDisposable
    {
        /// <summary>The commit the branch is at.</summary>
        string Head();

        /// <summary>
        /// Every file under <paramref name="folder"/> at that commit, with the hash of each.
        ///
        /// **Whoever implements this refuses a listing it cannot vouch for.** A partial
        /// answer read as a whole one is a core quietly missing blocks, which is the one
        /// thing a comparison must never be handed.
        /// </summary>
        IReadOnlyList<RemoteFile> Under(string commit, string folder);

        /// <summary>One file's bytes, by the hash <see cref="Under"/> gave for it.</summary>
        byte[] Read(string hash);
    }

    /// <summary>
    /// One file in a remote repository: where it is, and the hash of what is in it.
    ///
    /// **The hash is the whole design.** It is git's own, over the content, so the same hash
    /// can be computed from the file already on disk - which is what lets the copy be its own
    /// index and a second download fetch nothing. See <see cref="Git.GitBlobSha"/>.
    /// </summary>
    public sealed class RemoteFile
    {
        public RemoteFile(string path, string hash, long size)
        {
            Path = path;
            Hash = hash;
            Size = size;
        }

        /// <summary>The path from the repository root, with forward slashes.</summary>
        public string Path { get; }

        public string Hash { get; }

        public long Size { get; }

        public override string ToString() => Path;
    }
}
