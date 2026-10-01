using System.Collections.Generic;

namespace BlockTemplate.Checking.SimaticMl
{
    /// <summary>
    /// What a walk of a rendered SimaticML document found that is numbered: every <c>ID</c>, in the
    /// order it is written, and every LAD or FBD network with its <c>UId</c>s.
    /// </summary>
    internal sealed class ScannedDocument
    {
        public List<NumberedAttribute> Ids { get; } = new List<NumberedAttribute>();

        public List<FlgNetwork> Networks { get; } = new List<FlgNetwork>();
    }
}
