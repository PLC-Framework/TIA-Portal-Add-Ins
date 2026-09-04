using System;
using System.Collections.Generic;

using S7PlcWebserverApi;

namespace Satellite.DataBlockSnapshot.Export
{
    /// <summary>One variable as it goes into the file.</summary>
    public sealed class SnapshotRow
    {
        public SnapshotRow(string variable, string dataType, object value, string error, bool readOnly)
        {
            Variable = variable;
            DataType = dataType;
            Value = value;
            Error = error;
            ReadOnly = readOnly;
        }

        /// <summary>Readable name including the parent chain: MyDB.Struct.Var.</summary>
        public string Variable { get; }

        /// <summary>What the CPU calls the type: bool, time, real, string...</summary>
        public string DataType { get; }

        /// <summary>bool, long, double or string. Null when the read failed.</summary>
        public object Value { get; }

        /// <summary>Null when the read succeeded.</summary>
        public string Error { get; }

        /// <summary>The CPU refuses writes to this variable.</summary>
        public bool ReadOnly { get; }

        public bool Succeeded => Error == null;
    }

    /// <summary>
    /// One capture of one data block, and everything a reader needs to judge it.
    ///
    /// The point of this type is a guarantee: <b>every variable that was browsed has a
    /// row</b>, holding either its value or the reason it could not be read. The
    /// reference implementation this was ported from drops the failures, which turns a
    /// partial capture into one that looks complete - the worst outcome for a file whose
    /// whole job is to record a configuration faithfully.
    /// </summary>
    public sealed class Snapshot
    {
        private Snapshot(
            string plcAddress, string dataBlock,
            DateTime startedAt, DateTime completedAt,
            bool truncated, IReadOnlyList<SnapshotRow> rows)
        {
            PlcAddress = plcAddress;
            DataBlock = dataBlock;
            StartedAt = startedAt;
            CompletedAt = completedAt;
            Truncated = truncated;
            Rows = rows;

            int failed = 0;
            foreach (SnapshotRow row in rows)
                if (!row.Succeeded) failed++;

            FailedCount = failed;
        }

        public string PlcAddress { get; }
        public string DataBlock { get; }

        /// <summary>When the read started and finished. A capture is a sweep, not an
        /// instant, so both are recorded rather than a single timestamp that would
        /// suggest the values were all taken at once.</summary>
        public DateTime StartedAt { get; }
        public DateTime CompletedAt { get; }

        /// <summary>The browse stopped at a limit, so variables are missing.</summary>
        public bool Truncated { get; }

        public IReadOnlyList<SnapshotRow> Rows { get; }
        public int FailedCount { get; }

        public TimeSpan Duration => CompletedAt - StartedAt;

        /// <summary>
        /// Join what was browsed with what was read, in browse order.
        ///
        /// Browse order is the order an engineer sees in TIA and it is stable across
        /// runs, which is what lets two captures of the same block be compared line by
        /// line. A variable the read never answered for still gets a row.
        /// </summary>
        public static Snapshot Create(
            string plcAddress,
            string dataBlock,
            DateTime startedAt,
            DateTime completedAt,
            BrowseResult browse,
            IReadOnlyList<PlcValue> values)
        {
            Dictionary<string, PlcValue> byPath = new Dictionary<string, PlcValue>(values.Count, StringComparer.Ordinal);
            foreach (PlcValue value in values)
                byPath[value.Path] = value;

            List<SnapshotRow> rows = new List<SnapshotRow>(browse.Variables.Count);

            foreach (PlcVariable variable in browse.Variables)
            {
                PlcValue value;
                if (!byPath.TryGetValue(variable.Path, out value))
                {
                    rows.Add(new SnapshotRow(
                        variable.Name, variable.DataType, null,
                        "The variable was browsed but never read.", variable.ReadOnly));
                    continue;
                }

                rows.Add(new SnapshotRow(
                    variable.Name, variable.DataType, value.Value, value.Error, variable.ReadOnly));
            }

            return new Snapshot(plcAddress, dataBlock, startedAt, completedAt, browse.Truncated, rows);
        }
    }
}
