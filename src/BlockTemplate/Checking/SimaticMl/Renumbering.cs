using System.Collections.Generic;
using System.Globalization;

using BlockTemplate.Checking.Text;
using BlockTemplate.Rendering;

namespace BlockTemplate.Checking.SimaticMl
{
    /// <summary>
    /// The numbers a rendered document is given back in TIA's own order, so a block made from a
    /// template is the block TIA would export: two of them compare line for line, and a diff
    /// between a generated block and an exported one shows what differs rather than numbering.
    ///
    /// - **Every network's UIds from 21, in <see cref="FlgNetwork.InTiaOrder"/>**, and every
    ///   connection given the new number of what it joined. <c>uid()</c> counts across the whole
    ///   file, so without this a second network would start where the first stopped.
    /// - **Every <c>ID</c> in hexadecimal from 0, in the order the document is written** - what all
    ///   37 real SimaticML exports under <c>.example\</c> do, measured. **Nothing in SimaticML refers
    ///   to an ID**, read off every attribute those exports carry, so whatever a template wrote there -
    ///   an ID repeated by a loop, one left as it was copied - is replaced and nothing is lost.
    ///
    /// Only for a document <see cref="NetworkCheck"/> found sound: a UId given twice has no single
    /// new number.
    /// </summary>
    internal static class Renumbering
    {
        public static List<TextEdit> Edits(ScannedDocument scanned)
        {
            List<TextEdit> edits = new List<TextEdit>();

            for (int i = 0; i < scanned.Ids.Count; i++)
                edits.Add(Edit(scanned.Ids[i], i.ToString("X", CultureInfo.InvariantCulture)));

            foreach (FlgNetwork network in scanned.Networks)
            {
                Dictionary<int, int> renumbered = new Dictionary<int, int>();
                int next = UidCounter.First;

                foreach (NumberedAttribute numbered in network.InTiaOrder)
                {
                    renumbered.Add(numbered.Number.Value, next);
                    edits.Add(Edit(numbered, next.ToString(CultureInfo.InvariantCulture)));
                    next++;
                }

                foreach (NumberedAttribute reference in network.References)
                    edits.Add(Edit(reference, renumbered[reference.Number.Value].ToString(CultureInfo.InvariantCulture)));
            }

            return edits;
        }

        private static TextEdit Edit(NumberedAttribute attribute, string number) =>
            new TextEdit(attribute.Offset, attribute.Length, number);
    }
}
