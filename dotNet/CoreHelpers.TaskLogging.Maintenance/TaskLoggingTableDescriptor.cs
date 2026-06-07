using System;
using System.Globalization;
using System.Linq;

namespace CoreHelpers.TaskLogging.Maintenance
{
    internal class TaskLoggingTableDescriptor
    {
        private static readonly string[] RotatableSuffixes =
        {
            "Tasks",
            "Messages",
            "TasksFailed",
            "TasksExternalIdLookup"
        };

        private TaskLoggingTableDescriptor(string tableName, DateTimeOffset month)
        {
            TableName = tableName;
            Month = month;
        }

        public string TableName { get; }

        public DateTimeOffset Month { get; }

        public static bool TryParse(string tableName, string taskLoggerPrefix, out TaskLoggingTableDescriptor? descriptor)
        {
            descriptor = null;

            if (string.IsNullOrWhiteSpace(tableName) || string.IsNullOrWhiteSpace(taskLoggerPrefix))
                return false;

            if (!tableName.StartsWith(taskLoggerPrefix, StringComparison.Ordinal))
                return false;

            var suffix = RotatableSuffixes
                .OrderByDescending(s => s.Length)
                .FirstOrDefault(s => tableName.EndsWith(s, StringComparison.Ordinal));
            if (suffix == null)
                return false;

            var monthToken = tableName.Substring(
                taskLoggerPrefix.Length,
                tableName.Length - taskLoggerPrefix.Length - suffix.Length);

            if (monthToken.Length != 6 || !int.TryParse(monthToken, out _))
                return false;

            if (!DateTimeOffset.TryParseExact(
                    monthToken,
                    "yyyyMM",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal,
                    out var parsedMonth))
                return false;

            descriptor = new TaskLoggingTableDescriptor(
                tableName,
                new DateTimeOffset(parsedMonth.Year, parsedMonth.Month, 1, 0, 0, 0, TimeSpan.Zero));
            return true;
        }
    }
}
