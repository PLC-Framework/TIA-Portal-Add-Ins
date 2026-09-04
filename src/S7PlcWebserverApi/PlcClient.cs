using System;
using System.Collections.Generic;
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
    /// <summary>The outcome of one call inside a batch: a value, or the reason it failed.</summary>
    internal sealed class RpcResult
    {
        public JToken Value { get; }
        public string Error { get; }
        public int? Code { get; }
        public bool Succeeded => Error == null;

        public RpcResult(JToken value, string error, int? code)
        {
            Value = value;
            Error = error;
            Code = code;
        }
    }

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

        // A POST carrying fifty envelopes is more work for the CPU than a single call,
        // so this is not the ten seconds a one-shot request would need.
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

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

        // -- envelopes --------------------------------------------------------

        /// <summary>One JSON-RPC envelope carrying a fresh correlation id.</summary>
        private JObject Envelope(string method, JObject parameters)
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

            return payload;
        }

        // -- one call ---------------------------------------------------------

        internal JToken Rpc(
            string method,
            JObject parameters = null,
            bool authenticated = true,
            bool allowRelogin = true)
        {
            JObject envelope = Envelope(method, parameters);

            JToken body;
            try
            {
                body = Post(envelope.ToString(Formatting.None), authenticated);
            }
            catch (PlcAuthException) when (authenticated && allowRelogin)
            {
                Renew();
                return Rpc(method, parameters, authenticated, allowRelogin: false);
            }

            JObject parsed = body as JObject;
            if (parsed == null)
                throw new PlcException($"{method} -> the PLC answered a batch to a single call.");

            JToken error = parsed["error"];
            if (error != null && error.Type != JTokenType.Null)
            {
                string message = error["message"]?.Value<string>() ?? "Unknown PLC error";
                int? code = error["code"]?.Value<int?>();

                if (authenticated && LooksLikeAuthFailure(message))
                {
                    if (!allowRelogin) throw new PlcAuthException(message, code);
                    Renew();
                    return Rpc(method, parameters, authenticated, allowRelogin: false);
                }

                throw new PlcException(Describe(method, message, code, error["data"]), code);
            }

            return parsed["result"];
        }

        // -- many calls in one POST -------------------------------------------

        /// <summary>
        /// Send several calls in a single request, which JSON-RPC 2.0 allows as an
        /// array of envelopes. Measured at 31x the throughput of one request per
        /// variable against a CPU reporting Api.Version 2.00906 - 150 ms versus
        /// 4,671 ms for the same 54 variables.
        ///
        /// This is NOT the "var": [list] form Siemens documents for PlcProgram.Read:
        /// that one answers Invalid Params on the same CPU, while batching works.
        ///
        /// Results are keyed by request id and never by position. The specification
        /// lets a server answer a batch in any order, and this CPU happening to keep
        /// the order is not something to build on: matching by index would assign
        /// values to the wrong variable the day one does not.
        /// </summary>
        internal Dictionary<int, RpcResult> RpcBatch(
            IReadOnlyList<JObject> envelopes, bool allowRelogin = true)
        {
            Dictionary<int, RpcResult> results = new Dictionary<int, RpcResult>(envelopes.Count);
            if (envelopes.Count == 0) return results;

            JArray request = new JArray();
            foreach (JObject envelope in envelopes) request.Add(envelope);

            JToken body;
            try
            {
                body = Post(request.ToString(Formatting.None), authenticated: true);
            }
            catch (PlcAuthException) when (allowRelogin)
            {
                Renew();
                return RpcBatch(envelopes, allowRelogin: false);
            }

            // A batch of one comes back as a bare object on some firmwares.
            JArray entries = body as JArray ?? new JArray(body);

            foreach (JToken entry in entries)
            {
                JObject item = entry as JObject;
                int? id = item?["id"]?.Value<int?>();
                if (id == null) continue;

                JToken error = item["error"];
                if (error != null && error.Type != JTokenType.Null)
                {
                    string message = error["message"]?.Value<string>() ?? "Unknown PLC error";
                    int? code = error["code"]?.Value<int?>();

                    if (allowRelogin && LooksLikeAuthFailure(message))
                    {
                        Renew();
                        return RpcBatch(envelopes, allowRelogin: false);
                    }

                    results[id.Value] = new RpcResult(
                        null, Describe(item["method"]?.Value<string>() ?? "call", message, code, error["data"]), code);
                    continue;
                }

                results[id.Value] = new RpcResult(item["result"], null, null);
            }

            return results;
        }

        // -- transport --------------------------------------------------------

        private JToken Post(string json, bool authenticated)
        {
            HttpResponseMessage response;

            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, _url))
            {
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

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
                    throw new PlcAuthException("The PLC rejected the session token.");

                string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                try
                {
                    return JToken.Parse(body);
                }
                catch (JsonException exception)
                {
                    throw new PlcException(
                        $"The PLC returned a non-JSON response (HTTP {(int)response.StatusCode}).",
                        null, exception);
                }
            }
        }

        /// <summary>
        /// Replace an expired session. Callers repeat their call exactly once after
        /// this; without it, a token that ages out mid-snapshot aborts the whole run.
        /// </summary>
        private void Renew()
        {
            _token = null;
            Login();
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
