using Core.Checks;

namespace Satellite.CodingStyleReport.Report
{
    /// <summary>
    /// How an outcome reads to a person. One place, so the table and the exported workbook say
    /// exactly the same words - and so reading a workbook back can map them to one answer.
    /// </summary>
    public static class Outcomes
    {
        public static string Label(ReportRow row)
        {
            switch (row?.OutcomeValue)
            {
                case CheckOutcome.Passed: return "Passed";
                case CheckOutcome.Failed: return "Failed";
                case CheckOutcome.NotConfigured: return "Not configured";
                case CheckOutcome.Skipped: return "Skipped";
                default: return row?.Outcome;
            }
        }

        /// <summary>
        /// The outcome's name for a label read back from a workbook: the words above, or the
        /// enum's own names. Anything else is kept as it was written - the window shows an
        /// outcome it does not recognise rather than guessing one, and never filters it away.
        /// </summary>
        public static string Parse(string label)
        {
            string text = (label ?? string.Empty).Trim();

            foreach (CheckOutcome outcome in new[] { CheckOutcome.Passed, CheckOutcome.Failed, CheckOutcome.NotConfigured, CheckOutcome.Skipped })
            {
                ReportRow sample = new ReportRow { Outcome = outcome.ToString() };

                if (string.Equals(text, Label(sample), System.StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(text, outcome.ToString(), System.StringComparison.OrdinalIgnoreCase))
                {
                    return outcome.ToString();
                }
            }

            return text;
        }

        /// <summary>Failed, or anything that could not be judged: the two kinds of row worth a colour.</summary>
        public static bool IsFailure(ReportRow row) => row?.OutcomeValue == CheckOutcome.Failed;

        public static bool IsUnjudged(ReportRow row) =>
            row?.OutcomeValue == CheckOutcome.NotConfigured || row?.OutcomeValue == CheckOutcome.Skipped;
    }
}
