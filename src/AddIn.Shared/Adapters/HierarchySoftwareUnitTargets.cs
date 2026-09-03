using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AddIn.Shared.Adapters
{
    public sealed class HierarchySoftwareUnitTargets
    {
        public IGroupNode Blocks { get; set; }
        public IGroupNode TagTables { get; set; }
        public IGroupNode Types { get; set; }
    }
}
