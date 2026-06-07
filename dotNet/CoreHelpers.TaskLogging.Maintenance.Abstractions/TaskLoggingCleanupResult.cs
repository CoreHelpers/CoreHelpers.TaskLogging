using System.Collections.Generic;

namespace CoreHelpers.TaskLogging.Maintenance
{
    public class TaskLoggingCleanupResult
    {
        public TaskLoggingCleanupResult(
            bool rotationConfigured,
            int? logRetentionMonths,
            IReadOnlyList<string> matchingTables,
            IReadOnlyList<string> deletedTables,
            IReadOnlyList<string> skippedTables)
        {
            RotationConfigured = rotationConfigured;
            LogRetentionMonths = logRetentionMonths;
            MatchingTables = matchingTables;
            DeletedTables = deletedTables;
            SkippedTables = skippedTables;
        }

        public bool RotationConfigured { get; }

        public int? LogRetentionMonths { get; }

        public IReadOnlyList<string> MatchingTables { get; }

        public IReadOnlyList<string> DeletedTables { get; }

        public IReadOnlyList<string> SkippedTables { get; }
    }
}
