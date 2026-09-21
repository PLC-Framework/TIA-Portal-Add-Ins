using System;

namespace GitHubApi
{
    /// <summary>
    /// Which repository, on which host, as which caller: the four things `config.json`
    /// carries under <c>coreRemoteRepositoryConfig</c>, plus the token that never goes in it.
    ///
    /// **The token is handed in rather than read here.** It lives in the per-user `.env` as
    /// `${GITHUB_TOKEN}`, and reading a secret is `Core.Secrets`' job - this project knows
    /// nothing about where the framework keeps anything. It is also why this type is the
    /// only place the token appears: one field, passed in once.
    ///
    /// **No token is a legal state**, and it is how a public repository is read. What it is
    /// not is a way to read a private one: GitHub answers 404 rather than 401 to an
    /// unauthenticated caller, which the client turns back into something an operator can act
    /// on.
    /// </summary>
    public sealed class GitHubRepository
    {
        /// <summary>The public API, which is what `config.json` carries unless somebody runs Enterprise.</summary>
        public const string PublicApi = "https://api.github.com/";

        public GitHubRepository(string owner, string repository, string branch, string apiUrl = null, string token = null)
        {
            if (string.IsNullOrWhiteSpace(owner)) throw new ArgumentException("An owner is required.", nameof(owner));
            if (string.IsNullOrWhiteSpace(repository)) throw new ArgumentException("A repository is required.", nameof(repository));
            if (string.IsNullOrWhiteSpace(branch)) throw new ArgumentException("A branch is required.", nameof(branch));

            Owner = owner.Trim();
            Repository = repository.Trim();
            Branch = branch.Trim();
            Token = string.IsNullOrWhiteSpace(token) ? null : token.Trim();

            string api = string.IsNullOrWhiteSpace(apiUrl) ? PublicApi : apiUrl.Trim();

            // HttpClient's BaseAddress drops everything after the last slash when a relative
            // address is combined with it, so an api URL without one loses its last segment -
            // which on Enterprise is a path that matters.
            ApiUrl = api.EndsWith("/", StringComparison.Ordinal) ? api : api + "/";
        }

        public string Owner { get; }

        public string Repository { get; }

        public string Branch { get; }

        public string ApiUrl { get; }

        /// <summary>A personal access token, or null to read as nobody.</summary>
        public string Token { get; }

        public bool HasToken => Token != null;

        /// <summary>What a message calls this repository: <c>owner/repo@branch</c>.</summary>
        public override string ToString() => Owner + "/" + Repository + "@" + Branch;
    }
}
