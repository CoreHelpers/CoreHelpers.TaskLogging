namespace CoreHelpers.TaskLogging.Maintenance
{
    public static class TaskLoggingMaintenanceConstants
    {
        public const string SettingsTableSuffix = "Settings";
        public const string SettingsPartitionKey = "Maintenance";
        public const string SettingsRowKey = "LogCleanup";
        public const string LogRetentionMonthsProperty = "LogRetentionMonths";
    }
}
