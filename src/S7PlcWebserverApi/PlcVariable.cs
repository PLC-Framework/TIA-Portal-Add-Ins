using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace S7PlcWebserverApi
{
    /// <summary>One readable leaf found inside a data block.</summary>
    public sealed class PlcVariable
    {
        public PlcVariable(string path, string name, string dataType, bool readOnly)
        {
            Path = path;
            Name = name;
            DataType = dataType;
            ReadOnly = readOnly;
        }

        /// <summary>Quoted symbolic path the web API expects: "DB"."Struct"."Var".</summary>
        public string Path { get; }

        /// <summary>Readable name including the parent chain: DB.Struct.Var.</summary>
        public string Name { get; }

        public string DataType { get; }

        /// <summary>The CPU refuses writes to this variable.</summary>
        public bool ReadOnly { get; }
    }
}
