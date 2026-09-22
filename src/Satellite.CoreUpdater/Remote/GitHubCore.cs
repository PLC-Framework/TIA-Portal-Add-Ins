using System;
using System.Collections.Generic;

using Core.Config;
using Core.Repo.Remote;

using GitHubApi;

namespace Satellite.CoreUpdater.Remote
{
    /// <summary>
    /// `Core`'s remote-core port, answered with the GitHub client.
    ///
    /// **Thin on purpose.** What is worth testing - what to download, what to keep, what to
    /// delete - is `Core.Repo.Remote.RemoteCopy`, which a fake implementation of the port exercises
    /// without a network; and how to talk to GitHub is `GitHubApi`, which is exercised against
    /// a real repository without the framework. This file is the joint, and it is the one
    /// place that knows both names.
    ///
    /// **It lives here rather than in `Core` because of one assembly**: the client pulls
    /// `System.Net.Http`, and `Core` is loaded inside TIA Portal's process.
    /// </summary>
    public sealed class GitHubPlcCore : IRemotePlcCore
    {
        public IRemoteFiles Open(CoreRemoteRepositoryConfig repository, string token)
        {
            if (repository == null) throw new ArgumentNullException(nameof(repository));

            return new Files(new GitHubClient(
                new GitHubRepository(
                    repository.Owner, repository.Repository, repository.Branch, repository.ApiUrl, token)));
        }

        /// <summary>
        /// One open repository. **Every refusal keeps its own sentence**: the client already
        /// tells a missing token from a missing repository from the rate limit, and wrapping
        /// them in one message here would throw that away.
        /// </summary>
        private sealed class Files : IRemoteFiles
        {
            private readonly GitHubClient _client;

            public Files(GitHubClient client)
            {
                _client = client;
            }

            public string Head() => _client.Head();

            /// <summary>
            /// The files under one folder at one commit.
            ///
            /// **A tree GitHub could not give whole refuses here**, inside `Under`, rather
            /// than coming back short: a core read from a partial listing is one missing
            /// blocks with nothing saying so.
            /// </summary>
            public IReadOnlyList<RemoteFile> Under(string commit, string folder)
            {
                GitHubTree tree = _client.Tree(commit);
                IReadOnlyList<GitHubFile> found = tree.Under(folder);

                List<RemoteFile> files = new List<RemoteFile>(found.Count);

                foreach (GitHubFile file in found) files.Add(new RemoteFile(file.Path, file.Sha, file.Size));

                return files;
            }

            public byte[] Read(string hash) => _client.Read(hash);

            public void Dispose() => _client.Dispose();
        }
    }
}
