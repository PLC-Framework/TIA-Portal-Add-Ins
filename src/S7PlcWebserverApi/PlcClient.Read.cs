using System;
using System.Collections.Generic;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace S7PlcWebserverApi
{
    public sealed partial class PlcClient
    {
        // Firmware V2 refuses a list in "var" with "Invalid params". Learned once per
        // client, so the rest of the run goes straight to single reads instead of
        // paying one doomed request per batch.
        private bool _batchReads = true;

        /// <summary>
        /// Read variables in batches, isolating a failure to the offending tag: a
        /// variable the CPU rejects comes back with Error set and the rest still read.
        /// </summary>
        public IReadOnlyList<PlcValue> Read(IReadOnlyList<string> paths)
        {
            List<PlcValue> values = new List<PlcValue>();
            if (paths == null || paths.Count == 0) return values;

            for (int start = 0; start < paths.Count; start += PlcLimits.ReadBatchSize)
            {
                int size = Math.Min(PlcLimits.ReadBatchSize, paths.Count - start);
                List<string> chunk = new List<string>(size);
                for (int i = 0; i < size; i++) chunk.Add(paths[start + i]);

                values.AddRange(ReadChunk(chunk));
            }

            return values;
        }

        private IEnumerable<PlcValue> ReadChunk(List<string> chunk)
        {
            if (!_batchReads) return ReadIndividually(chunk);

            JToken result;
            try
            {
                result = Rpc("PlcProgram.Read", new JObject { ["var"] = new JArray(chunk) });
            }
            catch (PlcAuthException)
            {
                throw;                        // a dead session is not a per-tag problem
            }
            catch (PlcException)
            {
                _batchReads = false;
                return ReadIndividually(chunk);
            }

            // A CPU answering anything other than one entry per requested path is not
            // doing a batch read either, whatever it claims.
            if (!(result is JArray entries) || entries.Count != chunk.Count)
            {
                _batchReads = false;
                return ReadIndividually(chunk);
            }

            List<PlcValue> values = new List<PlcValue>(chunk.Count);
            for (int i = 0; i < chunk.Count; i++)
            {
                if (entries[i] is JObject item)
                    values.Add(new PlcValue(
                        item["var"]?.Value<string>() ?? chunk[i], ToClr(item["value"]), null));
                else
                    values.Add(new PlcValue(chunk[i], ToClr(entries[i]), null));
            }

            return values;
        }

        private IEnumerable<PlcValue> ReadIndividually(List<string> chunk)
        {
            List<PlcValue> values = new List<PlcValue>(chunk.Count);

            foreach (string path in chunk)
            {
                try
                {
                    values.Add(new PlcValue(path, ToClr(Rpc("PlcProgram.Read", new JObject { ["var"] = path })), null));
                }
                catch (PlcAuthException)
                {
                    throw;
                }
                catch (PlcException exception)
                {
                    values.Add(new PlcValue(path, null, exception.Message));
                }
            }

            return values;
        }

        /// <summary>
        /// Unwrap to Newtonsoft's own CLR value, so nothing outside this assembly needs
        /// to know about JToken. Keeps bool, long, double and string distinct, which is
        /// exactly what the exporters need to format a cell correctly.
        /// </summary>
        private static object ToClr(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return null;
            if (token is JValue value) return value.Value;
            return token.ToString(Formatting.None);
        }
    }
}
