using System;
using System.Collections.Generic;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace S7PlcWebserverApi
{
    public sealed partial class PlcClient
    {
        // Dropped to false the first time a batch POST fails outright, so the rest of
        // the run goes straight to single calls instead of paying a doomed round trip
        // per chunk. A CPU that rejects one batch rejects them all.
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

            // One envelope per variable, all of them in a single POST. See RpcBatch
            // for why this is not the "var": [list] form Siemens documents.
            List<JObject> envelopes = new List<JObject>(chunk.Count);
            int[] ids = new int[chunk.Count];

            for (int i = 0; i < chunk.Count; i++)
            {
                JObject envelope = Envelope("PlcProgram.Read", new JObject { ["var"] = chunk[i] });
                ids[i] = envelope["id"].Value<int>();
                envelopes.Add(envelope);
            }

            Dictionary<int, RpcResult> answers;
            try
            {
                answers = RpcBatch(envelopes);
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

            if (answers.Count == 0)
            {
                _batchReads = false;
                return ReadIndividually(chunk);
            }

            List<PlcValue> values = new List<PlcValue>(chunk.Count);

            for (int i = 0; i < chunk.Count; i++)
            {
                RpcResult answer;
                if (!answers.TryGetValue(ids[i], out answer))
                {
                    values.Add(new PlcValue(
                        chunk[i], null, "The PLC did not answer this variable in the batch."));
                    continue;
                }

                values.Add(answer.Succeeded
                    ? new PlcValue(chunk[i], ToClr(answer.Value), null)
                    : new PlcValue(chunk[i], null, answer.Error));
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
                    values.Add(new PlcValue(
                        path, ToClr(Rpc("PlcProgram.Read", new JObject { ["var"] = path })), null));
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

            JValue value = token as JValue;
            return value != null ? value.Value : token.ToString(Formatting.None);
        }
    }
}
