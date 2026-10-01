using System.Collections.Generic;
using System.Linq;

namespace BlockTemplate.Checking.SimaticMl
{
    /// <summary>
    /// One LAD or FBD network - a <c>FlgNet</c> directly inside a <c>NetworkSource</c> - and the
    /// <c>UId</c>s in it, sorted by what they are. A network is where a UId means something: TIA
    /// starts every one at 21, and a wire names the parts it joins by theirs.
    ///
    /// **A GRAPH block's networks are not these**: its transitions, interlocks, supervisions and
    /// permanent operations each hold a <c>FlgNet</c> of their own, inside the <c>Graph</c> rather
    /// than the <c>NetworkSource</c>, and what else numbers there is stage 1.4's to measure.
    /// </summary>
    internal sealed class FlgNetwork
    {
        public FlgNetwork(int line)
        {
            Line = line;
        }

        /// <summary>The line of the rendered text the network starts on.</summary>
        public int Line { get; }

        /// <summary>Everything under <c>Parts</c> that carries a UId - an <c>Access</c>, a <c>Part</c>, a <c>Call</c>, an <c>Instance</c>.</summary>
        public List<NumberedAttribute> Parts { get; } = new List<NumberedAttribute>();

        /// <summary>An <c>OpenCon</c>: a pin left unconnected, which sits in a wire and is numbered with the parts.</summary>
        public List<NumberedAttribute> OpenCons { get; } = new List<NumberedAttribute>();

        public List<NumberedAttribute> Wires { get; } = new List<NumberedAttribute>();

        /// <summary>An <c>IdentCon</c> or a <c>NameCon</c>: not a UId of its own, but the one of what a wire joins.</summary>
        public List<NumberedAttribute> References { get; } = new List<NumberedAttribute>();

        /// <summary>A UId anywhere else in the network - somewhere none of TIA's own networks has one.</summary>
        public List<NumberedAttribute> Strays { get; } = new List<NumberedAttribute>();

        /// <summary>
        /// Every UId the network gives, in the order TIA numbers them: **the parts in the order they
        /// are written, then the open connections, then the wires.** Measured on 2026-10-01 over all
        /// 232 networks of the real exports under <c>.example\</c>, V20 and V21, LAD, FBD and the
        /// networks inside GRAPH blocks alike: not one numbered otherwise. An <c>OpenCon</c> sits
        /// inside a wire and is still numbered before every wire.
        /// </summary>
        public IEnumerable<NumberedAttribute> InTiaOrder => Parts.Concat(OpenCons).Concat(Wires);
    }
}
