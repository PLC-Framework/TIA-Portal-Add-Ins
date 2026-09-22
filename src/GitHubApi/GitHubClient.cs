using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

using Newtonsoft.Json.Linq;

namespace GitHubApi
{
    /// <summary>
    /// Reads a repository through GitHub's REST API: which commit a branch is at, what is in
    /// that commit's tree, and the bytes of one file.
    ///
    /// **Three calls, in that order, and the first one is what makes the rest cheap.** A core
    /// is two hundred and fifty files, so downloading it blindly is two hundred and fifty
    /// requests every time. The branch's commit answers "has anything moved at all" in one,
    /// and a file's blob sha answers "has *this* changed" without fetching it - so a second
    /// run against a repository nobody has touched costs a single request.
    ///
    /// **Synchronous on purpose**, like the CPU's web API client beside it: the work is
    /// linear and the caller keeps its UI thread free by running this on a worker, which is
    /// what the core updater's own thread already is. Do not call it from a dispatcher.
    ///
    /// **Every failure comes back as one of four exceptions**, because an operator does
    /// something different about each - see `GitHubExceptions`.
    /// </summary>
    public sealed class GitHubClient : IDisposable
    {
        /// <summary>
        /// Generous, because this is a download rather than a question: a hundred small files
        /// over a slow line is still one request at a time, and the one that times out is the
        /// one somebody has to start again.
        /// </summary>
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

        /// <summary>
        /// **The API refuses a request with no User-Agent**, with a 403 that says so only in
        /// the body. It asks for the product's own name, which is what this sends.
        /// </summary>
        private const string UserAgent = "PLC-Framework";

        /// <summary>
        /// GitHub versions its REST API by header, and pins the behaviour of every endpoint
        /// this uses. Without it a future default could change a field under us.
        /// </summary>
        private const string ApiVersion = "2022-11-28";

        private readonly GitHubRepository _repository;
        private readonly HttpClient _http;

        public GitHubClient(GitHubRepository repository) : this(repository, DefaultTimeout)
        {
        }

        public GitHubClient(GitHubRepository repository, TimeSpan timeout)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));

            _http = new HttpClient
            {
                BaseAddress = new Uri(_repository.ApiUrl),
                Timeout = timeout > TimeSpan.Zero ? timeout : DefaultTimeout
            };

            _http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", ApiVersion);

            if (_repository.HasToken)
                _http.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", _repository.Token);
        }

        public GitHubRepository Repository => _repository;

        /// <summary>
        /// The commit the branch is at.
        ///
        /// **This is the cheap question, and the one worth asking first**: held against the
        /// commit a previous run wrote down, it says whether anything needs downloading at
        /// all.
        /// </summary>
        public string Head()
        {
            JObject branch;

            try
            {
                branch = Json("repos/" + Owner + "/" + Name + "/branches/" + Escaped(_repository.Branch));
            }
            catch (GitHubException refusal) when (refusal.Status == 404)
            {
                // **A 404 here has two meanings and they send an operator to different
                // places**: the repository is not readable, or the branch is not there. One
                // more request settles it, and only on the way to failing - so it costs
                // nothing on any run that works.
                throw Missing(refusal);
            }

            string commit = branch?["commit"]?["sha"]?.Value<string>();

            if (string.IsNullOrWhiteSpace(commit))
                throw new GitHubException("GitHub did not say which commit '" + _repository + "' is at.");

            return commit;
        }

        /// <summary>
        /// Which half of a 404 on a branch it was: asks for the repository itself, and only
        /// then decides what to say. An answer means the repository is readable and the
        /// branch is what is missing; another refusal is the one the caller already had, which
        /// is kept rather than replaced - it is the one that knows about the token.
        /// </summary>
        private GitHubException Missing(GitHubException refusal)
        {
            try
            {
                Json("repos/" + Owner + "/" + Name);
            }
            catch (GitHubException)
            {
                return refusal;
            }

            return new GitHubNotFoundException(
                "'" + _repository.Owner + "/" + _repository.Repository +
                "' has no branch called '" + _repository.Branch + "'.");
        }

        /// <summary>
        /// Every file in one commit's tree, in a single call.
        ///
        /// **Recursive, because the alternative is a call per folder** - and a core is four
        /// levels deep in places. The one thing that can go wrong with it is silent, so it is
        /// carried rather than dropped: see <see cref="GitHubTree.Truncated"/>.
        ///
        /// **Only blobs.** A tree entry is a file, a folder or a submodule; the last two are
        /// not things to download, and a submodule's contents are not in this repository at
        /// all.
        /// </summary>
        public GitHubTree Tree(string commit)
        {
            if (string.IsNullOrWhiteSpace(commit))
                throw new ArgumentException("A commit is required.", nameof(commit));

            JObject tree = Json(
                "repos/" + Owner + "/" + Name + "/git/trees/" + Escaped(commit.Trim()) + "?recursive=1");

            List<GitHubFile> files = new List<GitHubFile>();
            JArray entries = tree?["tree"] as JArray;

            if (entries != null)
                foreach (JToken entry in entries)
                {
                    if (!string.Equals(entry?["type"]?.Value<string>(), "blob", StringComparison.Ordinal)) continue;

                    string path = entry["path"]?.Value<string>();
                    string sha = entry["sha"]?.Value<string>();

                    if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(sha)) continue;

                    files.Add(new GitHubFile(path, sha, entry["size"]?.Value<long>() ?? 0));
                }

            return new GitHubTree(commit.Trim(), files, tree?["truncated"]?.Value<bool>() ?? false);
        }

        /// <summary>
        /// One file's bytes, by its blob sha.
        ///
        /// **Asked for raw rather than as JSON.** The blobs endpoint answers base64 inside a
        /// document by default, which is a third more bytes over the wire and a decode on
        /// this side; `application/vnd.github.raw` gives the file itself. A core carries
        /// `.xlsx` workbooks, so this returns bytes and never a string - text is the caller's
        /// interpretation, and one of these files is a zip.
        /// </summary>
        public byte[] Read(string blobSha)
        {
            if (string.IsNullOrWhiteSpace(blobSha))
                throw new ArgumentException("A blob sha is required.", nameof(blobSha));

            using (HttpRequestMessage request = new HttpRequestMessage(
                HttpMethod.Get, "repos/" + Owner + "/" + Name + "/git/blobs/" + Escaped(blobSha.Trim())))
            {
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.raw"));

                using (HttpResponseMessage response = Send(request))
                {
                    Refused(response);

                    return response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                }
            }
        }

        public void Dispose() => _http.Dispose();

        private string Owner => Escaped(_repository.Owner);

        private string Name => Escaped(_repository.Repository);

        private JObject Json(string path)
        {
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, path))
            {
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

                using (HttpResponseMessage response = Send(request))
                {
                    Refused(response);

                    string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                    try
                    {
                        return JObject.Parse(body);
                    }
                    catch (Exception exception)
                    {
                        // A proxy's login page, or an api URL pointing at something that is
                        // not GitHub. Saying which is better than a parser's complaint about
                        // a character at position one.
                        throw new GitHubException(
                            "GitHub's answer was not JSON, so '" + _repository.ApiUrl +
                            "' may not be a GitHub API.", (int)response.StatusCode, exception);
                    }
                }
            }
        }

        private HttpResponseMessage Send(HttpRequestMessage request)
        {
            try
            {
                return _http.SendAsync(request).GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                // No network, no DNS, a proxy in the way, a TLS handshake that failed, or the
                // timeout. None of them is GitHub refusing anything, and an operator reads
                // them as one thing: it could not be reached.
                throw new GitHubConnectionException(
                    "GitHub could not be reached at '" + _repository.ApiUrl + "': " + Innermost(exception).Message,
                    exception);
            }
        }

        /// <summary>
        /// Turns a refusal into the one of four exceptions that says what to do about it.
        ///
        /// **A 404 without a token is reported as a token problem**, and that is the subtle
        /// one: GitHub answers 404 rather than 401 for a private repository read by nobody,
        /// so believing the status would send an operator to check a name that is correct.
        /// With a token, 404 means what it says.
        /// </summary>
        private void Refused(HttpResponseMessage response)
        {
            if (response.IsSuccessStatusCode) return;

            int status = (int)response.StatusCode;
            string said = Message(response);

            if (Limited(response))
                throw new GitHubLimitException(
                    "GitHub's rate limit has been reached" +
                    (_repository.HasToken ? "" : " - and without a token it is sixty calls an hour") + ". " + said,
                    Resets(response), status);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new GitHubAuthException("GitHub refused the token. " + said, status);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                if (!_repository.HasToken)
                    throw new GitHubAuthException(
                        "GitHub answered 'not found' for '" + _repository + "'" + Where() + ", which is also what " +
                        "it answers for a private repository read without a token. Set REPO_TOKEN, or check the name.",
                        status);

                throw new GitHubNotFoundException("GitHub has no '" + _repository + "'" + Where() + ". " + said);
            }

            if (response.StatusCode == HttpStatusCode.Forbidden)
                throw new GitHubAuthException(
                    "GitHub refused the request for '" + _repository + "'" +
                    (_repository.HasToken ? ", which usually means the token cannot read it" : "") + ". " + said,
                    status);

            throw new GitHubException("GitHub answered " + status + " for '" + _repository + "'. " + said, status);
        }

        /// <summary>
        /// Where the call went, **named only when it is not the public API**.
        ///
        /// A 404 from `api.github.com` is about the repository; one from an `apiUrl` somebody
        /// typed into `config.json` may be about that URL, and a message that never mentions
        /// it sends them to check a name that was right all along. The common case says
        /// nothing, because naming the default would be noise on every message.
        /// </summary>
        private string Where() =>
            string.Equals(_repository.ApiUrl, GitHubRepository.PublicApi, StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : " at '" + _repository.ApiUrl + "'";

        /// <summary>
        /// The rate limit, which arrives as a 403 or a 429 and is told apart from any other
        /// refusal by the header GitHub sends with it.
        /// </summary>
        private static bool Limited(HttpResponseMessage response)
        {
            if (response.StatusCode != HttpStatusCode.Forbidden && (int)response.StatusCode != 429) return false;

            IEnumerable<string> left;

            if (response.Headers.TryGetValues("x-ratelimit-remaining", out left))
                foreach (string value in left)
                    if (value == "0") return true;

            return response.Headers.Contains("retry-after");
        }

        private static DateTime? Resets(HttpResponseMessage response)
        {
            IEnumerable<string> values;

            if (!response.Headers.TryGetValues("x-ratelimit-reset", out values)) return null;

            foreach (string value in values)
            {
                long seconds;

                if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out seconds))
                    return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(seconds);
            }

            return null;
        }

        /// <summary>GitHub's own sentence about the refusal, when the body carries one.</summary>
        private static string Message(HttpResponseMessage response)
        {
            try
            {
                string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                if (string.IsNullOrWhiteSpace(body)) return string.Empty;

                return JObject.Parse(body)["message"]?.Value<string>() ?? string.Empty;
            }
            catch (Exception)
            {
                // The body is not JSON, or there is none. The status already said the half
                // that matters.
                return string.Empty;
            }
        }

        private static Exception Innermost(Exception exception)
        {
            while (exception.InnerException != null) exception = exception.InnerException;

            return exception;
        }

        /// <summary>
        /// **A branch name can hold a slash** - `release/1.2` is an ordinary one - and a sha
        /// cannot, but both go into a path here. Escaping the segment keeps a branch from
        /// reading as two of them.
        /// </summary>
        private static string Escaped(string value) => Uri.EscapeDataString(value);
    }
}
