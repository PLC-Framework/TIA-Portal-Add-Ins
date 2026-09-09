using System.Collections.Generic;
using System.Linq;

namespace Core.Config.Validation
{
    /// <summary>
    /// Everything wrong with a configuration, not just the first thing.
    ///
    /// That is the whole reason validation is a separate pass rather than something the
    /// serializer does: a serializer that throws stops at the first problem and loses the
    /// rest, so fixing a file by hand becomes one round trip per mistake.
    /// </summary>
    public sealed class ValidationResult
    {
        private static readonly ValidationIssue[] None = new ValidationIssue[0];

        public static readonly ValidationResult Valid = new ValidationResult(None);

        public ValidationResult(IReadOnlyList<ValidationIssue> issues)
        {
            Issues = issues ?? None;
        }

        public IReadOnlyList<ValidationIssue> Issues { get; }

        public bool IsValid => Issues.Count == 0;

        /// <summary>
        /// The issues as lines, ready to put in a message box or a log. Ordered as they
        /// were found, which is document order.
        /// </summary>
        public IEnumerable<string> Lines() => Issues.Select(issue => issue.ToString());

        public override string ToString() =>
            IsValid ? "Valid." : string.Join("\r\n", Lines());
    }
}
