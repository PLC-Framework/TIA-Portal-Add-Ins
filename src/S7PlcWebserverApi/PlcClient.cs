using Newtonsoft.Json.Linq;
using System;

namespace S7PlcWebserverApi
{
    public sealed class PlcClient : IDisposable
    {
        public PlcClient(string ip, string user, string password);

        public string Ip { get; }
        public bool IsLoggedIn { get; }
        public void Login();
        public void Logout();

        internal JToken Rpc(string method, JObject parameters = null);
    }
}
