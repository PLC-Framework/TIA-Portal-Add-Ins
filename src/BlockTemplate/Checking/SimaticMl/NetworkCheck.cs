using System.Collections.Generic;

namespace BlockTemplate.Checking.SimaticMl
{
    /// <summary>
    /// What makes a LAD or FBD network one TIA can read, and one that can be put in TIA's order:
    ///
    /// - **Every UId is a whole number** - digits only, as TIA writes them.
    /// - **No two things in one network share a UId.** A wire names what it joins by its UId, so
    ///   two parts with one number are a wire that could mean either - and nothing to renumber by.
    /// - **Every connection names something the network has, and something of its kind**: an
    ///   <c>IdentCon</c> an <c>Access</c>, a <c>NameCon</c> a <c>Part</c> or a <c>Call</c>, measured
    ///   on every network of the real exports. A <c>uid("key")</c> mistyped in one place is a wire to
    ///   nowhere, and TIA would say so about a file the operator never wrote.
    /// - **A UId only where TIA's networks have one**: under <c>Parts</c>, on a <c>Wire</c>, on an
    ///   <c>OpenCon</c>, on a connection. Anywhere else there is no measured place for it in TIA's
    ///   order, and putting it somewhere would be a guess.
    /// </summary>
    internal static class NetworkCheck
    {
        public static void Check(FlgNetwork network, string file, List<TemplateProblem> problems)
        {
            Dictionary<int, NumberedAttribute> given = new Dictionary<int, NumberedAttribute>();

            foreach (NumberedAttribute stray in network.Strays)
                problems.Add(Problem(file, stray, "<" + stray.Element + "> carries a UId, which TIA's networks give only to what is under " +
                                                  "<Parts>, to a <Wire> and to an <OpenCon> - there is no place for it in TIA's order."));

            foreach (NumberedAttribute numbered in network.InTiaOrder)
            {
                int? number = numbered.Number;
                if (number == null)
                {
                    problems.Add(NotANumber(file, numbered));
                    continue;
                }

                if (given.TryGetValue(number.Value, out NumberedAttribute first))
                {
                    problems.Add(Problem(file, numbered, "UId " + number + " is given to <" + numbered.Element + "> and, at rendered line " + first.Line +
                                                         ", to <" + first.Element + ">: one network cannot have two of either."));
                    continue;
                }

                given.Add(number.Value, numbered);
            }

            foreach (NumberedAttribute reference in network.References)
            {
                int? number = reference.Number;
                if (number == null)
                {
                    problems.Add(NotANumber(file, reference));
                    continue;
                }

                if (!given.TryGetValue(number.Value, out NumberedAttribute target))
                {
                    problems.Add(Problem(file, reference, "<" + reference.Element + "> connects UId " + number + ", which nothing in this network has."));
                    continue;
                }

                bool fits = reference.Element == SimaticMlNames.IdentCon
                    ? target.Element == SimaticMlNames.Access
                    : target.Element == SimaticMlNames.Part || target.Element == SimaticMlNames.Call;

                if (!fits)
                    problems.Add(Problem(file, reference, "<" + reference.Element + "> connects UId " + number + ", which is <" + target.Element +
                                                          "> at rendered line " + target.Line + " - an <IdentCon> connects an <Access>, " +
                                                          "a <NameCon> a pin of a <Part> or a <Call>."));
            }
        }

        private static TemplateProblem NotANumber(string file, NumberedAttribute at) =>
            Problem(file, at, "The UId of <" + at.Element + "> is '" + at.Value + "', which is not a UId TIA would write: digits alone, " +
                              "and no more than a whole number holds.");

        private static TemplateProblem Problem(string file, NumberedAttribute at, string message) =>
            new TemplateProblem(file, at.Line, message, rendered: true);
    }
}
