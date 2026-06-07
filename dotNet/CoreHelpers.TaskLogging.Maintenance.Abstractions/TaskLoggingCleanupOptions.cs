using System;

namespace CoreHelpers.TaskLogging.Maintenance
{
    public class TaskLoggingCleanupOptions
    {
        public string ConnectionString { get; set; } = string.Empty;

        public string TaskLoggerPrefix { get; set; } = string.Empty;

        public bool DryRun { get; set; } = true;

        public DateTimeOffset? ReferenceDateUtc { get; set; }
    }
}
