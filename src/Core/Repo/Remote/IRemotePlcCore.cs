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
    /// resolving `${REPO_TOKEN}` out of the per-user `.env` is `Core.Secrets`' job.
    /// </summary>
    public interface IRemotePlcCore
    {
        /// <param name="token">
        /// The token, already resolved - or null, which is how a public repository is read.
        /// </param>
        IRemoteFiles Open(CoreRemoteRepositoryConfig repository, string token);
    }
}
