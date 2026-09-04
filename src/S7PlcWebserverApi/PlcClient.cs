using System;
using System.Net;
using System.Net.Http;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace S7PlcWebserverApi
{
    /// <summary>
    /// One web API session against a single S7-1200 / S7-1500 CPU.
    ///
    /// Synchronous on purpose: the work is linear - login, browse, read - so async
    /// would buy nothing except keeping a UI thread free, which the caller gets by
    /// running this on a background thread instead. Do not call it from the WPF
    /// dispatcher thread.
    /// </summary>
    public sealed partial class PlcClient : IDisposable
    {
        private const string JsonRpcPath = "/api/jsonrpc";
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

        // The web API has no dedicated code for an expired token, so the message
        // text is all there is to go on. This list is empirical.
        private static readonly string[] AuthHints =
        {
            "token", "session", "login", "authent",
            "permission", "not authorized", "unauthorized"
        };

        private readonly string _user;
        private readonly string _password;
        private readonly string _url;
        private readonly HttpClient _http;
        private string _token;
        private int _nextId;
        public string Ip { get; }
        public bool IsLoggedIn => _token != null;

        public PlcClient(string ip, string user, string password)
        {
            if (string.IsNullOrWhiteSpace(ip))
                throw new ArgumentException("An IP address is required.", nameof(ip));

            Ip = ip.Trim();
            _user = user;
            _password = password;
            _url = "https://" + Ip + JsonRpcPath;

            // Both settings are deliberately scoped to this handler. Their
            // ServicePointManager equivalents would change TLS negotiation and
            // certificate validation for every connection the process makes.
            HttpClientHandler handler = new HttpClientHandler
            {
                SslProtocols = SslProtocols.Tls12,
                ServerCertificateCustomValidationCallback = (m, certificate, chain, errors) => true
            };

            _http = new HttpClient(handler) { Timeout = RequestTimeout };
        }

        public void Login()
        {
            JObject parameters = new JObject
            {
                ["user"] = _user,
                ["password"] = _password
            };

            JToken result = Rpc("Api.Login", parameters, authenticated: false);
            string token = result?["token"]?.Value<string>();

            if (string.IsNullOrEmpty(token))
                throw new PlcAuthException("The PLC did not return a session token.");

            _token = token;
        }

        public void Logout()
        {
            if (_token == null) return;

            try
            {
                Rpc("Api.Logout", allowRelogin: false);
            }
            catch (PlcException)
            {
                // A stale session expires on the CPU by itself; nothing to report.
            }
            finally
            {
                _token = null;
            }
        }

        public void Dispose()
        {
            Logout();
            _http.Dispose();
        }

        internal JToken Rpc(
            string method,
            JObject parameters = null,
            bool authenticated = true,
            bool allowRelogin = true)
        {
            JObject payload = new JObject
            {
                ["jsonrpc"] = "2.0",
                ["method"] = method,
                ["id"] = Interlocked.Increment(ref _nextId)
            };

            // Only send "params" when there is something in it: some firmwares
            // reject an empty object with "Invalid params".
            if (parameters != null && parameters.Count > 0)
                payload["params"] = parameters;

            HttpResponseMessage response;
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, _url))
            {
                request.Content = new StringContent(
                    payload.ToString(Formatting.None), Encoding.UTF8, "application/json");

                if (authenticated && _token != null)
                    request.Headers.Add("X-Auth-Token", _token);

                try
                {
                    response = _http.SendAsync(request).GetAwaiter().GetResult();
                }
                catch (Exception exception)
                    when (exception is HttpRequestException || exception is TaskCanceledException)
                {
                    throw new PlcConnectionException(
                        $"Cannot reach the PLC at {Ip}: {exception.Message}", exception);
                }
            }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                    return Renew(method, parameters, authenticated, allowRelogin,
                                 "The PLC rejected the session token.", null);

                string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                JObject parsed;
                try
                {
                    parsed = JObject.Parse(body);
                }
                catch (JsonException exception)
                {
                    throw new PlcException(
                        $"The PLC returned a non-JSON response (HTTP {(int)response.StatusCode}).",
                        null, exception);
                }

                JToken error = parsed["error"];
                if (error != null && error.Type != JTokenType.Null)
                {
                    string message = error["message"]?.Value<string>() ?? "Unknown PLC error";
                    int? code = error["code"]?.Value<int?>();

                    if (authenticated && LooksLikeAuthFailure(message))
                        return Renew(method, parameters, authenticated, allowRelogin, message, code);

                    throw new PlcException(Describe(method, message, code, error["data"]), code);
                }

                return parsed["result"];
            }
        }

        /// <summary>
        /// Replace an expired session and repeat the call exactly once. Without this,
        /// a token that ages out mid-snapshot aborts the whole run.
        /// </summary>
        private JToken Renew(
            string method, JObject parameters, bool authenticated,
            bool allowRelogin, string message, int? code)
        {
            if (!allowRelogin)
                throw new PlcAuthException(message, code);

            _token = null;
            Login();
            return Rpc(method, parameters, authenticated, allowRelogin: false);
        }

        private static bool LooksLikeAuthFailure(string message)
        {
            if (string.IsNullOrEmpty(message)) return false;

            string lowered = message.ToLowerInvariant();
            foreach (string hint in AuthHints)
                if (lowered.Contains(hint)) return true;

            return false;
        }

        /// <summary>
        /// Name the call that failed. The CPU often answers a bare "Invalid params",
        /// which is impossible to act on without knowing which method produced it.
        /// </summary>
        private static string Describe(string method, string message, int? code, JToken data)
        {
            StringBuilder text = new StringBuilder(method).Append(" -> ").Append(message);

            if (code.HasValue)
                text.Append(", code ").Append(code.Value);

            if (data != null && data.Type != JTokenType.Null)
                text.Append(", ").Append(data.ToString(Formatting.None));

            return text.ToString();
        }
    }
}
