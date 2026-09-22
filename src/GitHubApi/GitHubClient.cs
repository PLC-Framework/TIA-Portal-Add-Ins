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

            // **Redirects are not followed, and that is the whole of a bug worth remembering.**
            // GitHub answers 301 for a repository or an owner that has been renamed - and
            // `HttpClient` on .NET Framework drops the `Authorization` header when it follows
            // one. The followed request therefore arrives anonymous, so GitHub replies with the
            // anonymous rate limit or a 404, and the operator is sent to check a token that is
            // perfectly good or a name that is merely old. Measured against a real renamed
            // owner: /rate_limit with the same token answers 5,000 an hour while the redirected
            // call answers "rate limit exceeded for <our IP>".
            //
            // Following it with the header re-attached would work and is still wrong: the core's
            // identity is the one in `config.json`, and a silent follow leaves that file stale
            // for good while `core.origin.json` records a repository the configuration does not
            // name. So a move is reported, by name, and somebody edits one line.
            HttpClientHandler handler = new HttpClientHandler { AllowAutoRedirect = false };

            _http = new HttpClient(handler)
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

        private static bool Redirected(HttpResponseMessage response)
        {
            int status = (int)response.StatusCode;

            return status == 301 || status == 302 || status == 307 || status == 308;
        }

        /// <summary>
        /// A repository that has moved, named on both sides where that can be done.
        ///
        /// **GitHub does not say the new name in the `Location` header** - it answers
        /// <c>…/repositories/1301869263</c>, the repository's numeric id, which is no use to
        /// anybody editing a configuration file. So this asks that one URL what it is called,
        /// **once, and only on the way to failing**, which is the same bargain `Missing` already
        /// strikes: it costs nothing on any run that works.
        ///
        /// **A second request that goes wrong costs the destination, never the message.** Not
        /// knowing where it went is worth far less than knowing it moved, and a redirect chain
        /// or a refusal on the way must not turn a clear sentence into an unclear one.
        /// </summary>
        private GitHubMovedException Moved(HttpResponseMessage response)
        {
            Uri location = response.Headers.Location;
            string now = NamedAt(location);
            int status = (int)response.StatusCode;

            string said =
                "GitHub says '" + _repository + "'" + Where() + " has moved" +
                (now == null ? string.Empty : ", and is now '" + now + "'") +
                ". Somebody renamed the repository or its owner, so update owner and repository " +
                "in config.json - this framework does not follow the move on its own, because " +
                "then the file would stay wrong.";

            return new GitHubMovedException(said, now, status);
        }

        /// <summary>
        /// The repository itself, out of wherever the redirect pointed.
        ///
        /// **A redirect keeps the rest of the path**, which is the detail that made the first
        /// version of this answer nothing: asking for a branch of a renamed repository is sent
        /// to <c>…/repositories/1301869263/branches/main</c>, and a branch has no
        /// <c>full_name</c>. What carries the name is the repository, two segments up.
        /// </summary>
        private static Uri RepositoryAt(Uri location)
        {
            if (location == null || !location.IsAbsoluteUri) return null;

            string[] parts = location.AbsolutePath.Trim('/').Split('/');

            for (int i = 0; i + 1 < parts.Length; i++)
            {
                // Either shape GitHub can send us back to: the id it answers for a rename, and
                // the plain name a differently configured host might.
                if (!string.Equals(parts[i], "repositories", StringComparison.Ordinal) &&
                    !string.Equals(parts[i], "repos", StringComparison.Ordinal)) continue;

                int keep = string.Equals(parts[i], "repos", StringComparison.Ordinal) ? 3 : 2;

                if (i + keep > parts.Length) break;

                return new Uri(
                    location.GetLeftPart(UriPartial.Authority) + "/" +
                    string.Join("/", parts, i, keep));
            }

            // Not a shape this knows: ask where it was pointed and let the answer decide.
            return location;
        }

        /// <summary>What the repository behind a redirect calls itself, or null.</summary>
        private string NamedAt(Uri location)
        {
            Uri repository = RepositoryAt(location);

            if (repository == null) return null;

            try
            {
                using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, repository))
                {
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

                    using (HttpResponseMessage response = Send(request))
                    {
                        if (!response.IsSuccessStatusCode) return null;

                        string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                        string name = JObject.Parse(body)["full_name"]?.Value<string>();

                        return string.IsNullOrWhiteSpace(name) ? null : name;
                    }
                }
            }
            catch (Exception)
            {
                // See above: the destination is a courtesy, the move is the message.
                return null;
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

            // Asked before anything else, because a redirect is not a refusal at all: the
            // repository is there and the configuration is pointing at where it used to be.
            if (Redirected(response)) throw Moved(response);

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
