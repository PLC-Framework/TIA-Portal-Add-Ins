using System;

namespace GitHubApi
{
    /// <summary>
    /// A call reached GitHub and GitHub refused it.
    ///
    /// **The four kinds are kept apart because the operator does something different about
    /// each**: a token that is missing or wrong is a `.env` to fix, a 404 is a name in
    /// `config.json` to correct, a rate limit is a wait, and a connection failure is the
    /// network. One exception type carrying four meanings would leave the window with one
    /// sentence for all of them.
    /// </summary>
    public class GitHubException : Exception
    {
        public GitHubException(string message, int? status = null, Exception inner = null)
            : base(message, inner) => Status = status;

        /// <summary>The HTTP status GitHub answered with, when it answered.</summary>
        public int? Status { get; }
    }

    /// <summary>
    /// No token where one is needed, or one GitHub will not accept.
    ///
    /// **A private repository answers 404 to an unauthenticated caller**, deliberately - it
    /// will not confirm that it exists. So a 404 with no token is reported as this rather
    /// than as "no such repository", which would send somebody to check a name that is
    /// perfectly correct.
    /// </summary>
    public sealed class GitHubAuthException : GitHubException
    {
        public GitHubAuthException(string message, int? status = null, Exception inner = null)
            : base(message, status, inner) { }
    }

    /// <summary>The owner, the repository, the branch or the object is not there.</summary>
    /// <summary>
    /// The repository is still there and is not where the configuration says: somebody renamed
    /// it, or its owner.
    ///
    /// **Its own exception because it is its own instruction** - not a token to check and not a
    /// name to doubt, but one line of `config.json` to bring up to date.
    /// </summary>
    public sealed class GitHubMovedException : GitHubException
    {
        public GitHubMovedException(string message, string movedTo, int? status = null)
            : base(message, status)
        {
            MovedTo = movedTo;
        }

        /// <summary>Where it went, as <c>owner/repository</c> - or null when that could not be read.</summary>
        public string MovedTo { get; }
    }

    public sealed class GitHubNotFoundException : GitHubException
    {
        public GitHubNotFoundException(string message, Exception inner = null)
            : base(message, 404, inner) { }
    }

    /// <summary>
    /// The rate limit, with the moment it lifts - **which is the only thing worth saying
    /// about it**: sixty calls an hour unauthenticated, five thousand with a token, and a
    /// core of two hundred and fifty files is well inside the second and well outside the
    /// first.
    /// </summary>
    public sealed class GitHubLimitException : GitHubException
    {
        public GitHubLimitException(string message, DateTime? until, int? status = null)
            : base(message, status) => Until = until;

        /// <summary>When the limit resets, in UTC, when GitHub said.</summary>
        public DateTime? Until { get; }
    }

    /// <summary>GitHub was not reached at all: no network, no DNS, a proxy, a closed port.</summary>
    public sealed class GitHubConnectionException : GitHubException
    {
        public GitHubConnectionException(string message, Exception inner = null)
            : base(message, null, inner) { }
    }
}
