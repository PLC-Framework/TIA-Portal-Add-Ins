using System.Collections.Generic;

using BlockTemplate.Checking.Text;

namespace BlockTemplate.Checking.SimaticMl
{
    /// <summary>
    /// A rendered SimaticML document checked and put in TIA's order: well-formed, every LAD or FBD
    /// network sound - see <see cref="NetworkCheck"/> - and then renumbered - see
    /// <see cref="Renumbering"/>. The text back, or null with every problem said.
    /// </summary>
    internal static class SimaticMlCheck
    {
        public static string Check(string text, string file, List<TemplateProblem> problems)
        {
            int before = problems.Count;

            ScannedDocument scanned = SimaticMlScan.Scan(text, file, problems);
            if (scanned == null) return null;

            foreach (FlgNetwork network in scanned.Networks)
                NetworkCheck.Check(network, file, problems);

            if (problems.Count > before) return null;

            return TextEdit.Apply(text, Renumbering.Edits(scanned));
        }
    }
}
