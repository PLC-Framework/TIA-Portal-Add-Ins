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

        /// <summary>Failed, or anything that could not be judged: the two kinds of row worth a colour.</summary>
        public static bool IsFailure(ReportRow row) => row?.OutcomeValue == CheckOutcome.Failed;

        public static bool IsUnjudged(ReportRow row) =>
            row?.OutcomeValue == CheckOutcome.NotConfigured || row?.OutcomeValue == CheckOutcome.Skipped;
    }
}
