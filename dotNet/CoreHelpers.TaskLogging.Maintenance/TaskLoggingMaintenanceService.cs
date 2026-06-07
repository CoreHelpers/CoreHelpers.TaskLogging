using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure.Data.Tables;

namespace CoreHelpers.TaskLogging.Maintenance
{
    internal class TaskLoggingMaintenanceService : ITaskLoggingMaintenanceService
    {
        private readonly Func<string, ITaskLoggingTableStorage> _storageFactory;

        internal TaskLoggingMaintenanceService()
            : this(connectionString => new AzureTaskLoggingTableStorage(connectionString))
        {
        }

        internal TaskLoggingMaintenanceService(Func<string, ITaskLoggingTableStorage> storageFactory)
        {
            _storageFactory = storageFactory;
        }

        public async Task<TaskLoggingCleanupResult> CleanupAsync(
            TaskLoggingCleanupOptions options,
            CancellationToken cancellationToken = default)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            if (string.IsNullOrWhiteSpace(options.ConnectionString))
                throw new ArgumentException("Connection string is required.", nameof(options));

            if (string.IsNullOrWhiteSpace(options.TaskLoggerPrefix))
                throw new ArgumentException("Task logger prefix is required.", nameof(options));

            var storage = _storageFactory(options.ConnectionString);
            var tableNames = await storage.ListTableNamesAsync(cancellationToken);
            var matchingTables = tableNames
                .Select(t => TaskLoggingTableDescriptor.TryParse(t, options.TaskLoggerPrefix, out var descriptor) ? descriptor : null)
                .Where(t => t != null)
                .Cast<TaskLoggingTableDescriptor>()
                .OrderBy(t => t.TableName, StringComparer.Ordinal)
                .ToList();

            var logRetentionMonths = await ReadRetentionMonths(storage, options.TaskLoggerPrefix, cancellationToken);
            if (!logRetentionMonths.HasValue || logRetentionMonths.Value <= 0)
            {
                return new TaskLoggingCleanupResult(
                    false,
                    logRetentionMonths,
                    matchingTables.Select(t => t.TableName).ToList(),
                    Array.Empty<string>(),
                    matchingTables.Select(t => t.TableName).ToList());
            }

            var cutoffMonth = GetRetentionCutoffMonth(options.ReferenceDateUtc ?? DateTimeOffset.UtcNow, logRetentionMonths.Value);
            var tablesToDelete = matchingTables
                .Where(t => t.Month < cutoffMonth)
                .ToList();

            var deleteCandidates = tablesToDelete.Select(t => t.TableName).ToList();
            var deletedTables = new List<string>();
            if (!options.DryRun)
            {
                foreach (var table in tablesToDelete)
                {
                    await storage.DeleteTableAsync(table.TableName, cancellationToken);
                    deletedTables.Add(table.TableName);
                }
            }

            var skippedTables = matchingTables
                .Select(t => t.TableName)
                .Except(deleteCandidates, StringComparer.Ordinal)
                .ToList();

            return new TaskLoggingCleanupResult(
                true,
                logRetentionMonths,
                matchingTables.Select(t => t.TableName).ToList(),
                options.DryRun ? deleteCandidates : deletedTables,
                skippedTables);
        }

        internal static DateTimeOffset GetRetentionCutoffMonth(DateTimeOffset referenceDateUtc, int logRetentionMonths)
        {
            var currentMonth = new DateTimeOffset(
                referenceDateUtc.UtcDateTime.Year,
                referenceDateUtc.UtcDateTime.Month,
                1,
                0,
                0,
                0,
                TimeSpan.Zero);

            return currentMonth.AddMonths(-logRetentionMonths);
        }

        private static async Task<int?> ReadRetentionMonths(
            ITaskLoggingTableStorage storage,
            string taskLoggerPrefix,
            CancellationToken cancellationToken)
        {
            var settingsTableName = $"{taskLoggerPrefix}{TaskLoggingMaintenanceConstants.SettingsTableSuffix}";
            var settings = await storage.GetSettingsEntityAsync(
                settingsTableName,
                TaskLoggingMaintenanceConstants.SettingsPartitionKey,
                TaskLoggingMaintenanceConstants.SettingsRowKey,
                cancellationToken);

            if (settings == null ||
                !settings.TryGetValue(TaskLoggingMaintenanceConstants.LogRetentionMonthsProperty, out var retentionValue))
                return null;

            return TryConvertRetentionMonths(retentionValue, out var retentionMonths)
                ? (int?)retentionMonths
                : null;
        }

        private static bool TryConvertRetentionMonths(object retentionValue, out int retentionMonths)
        {
            switch (retentionValue)
            {
                case int intValue:
                    retentionMonths = intValue;
                    return true;
                case long longValue when longValue <= int.MaxValue && longValue >= int.MinValue:
                    retentionMonths = (int)longValue;
                    return true;
                case string stringValue when int.TryParse(stringValue, out var parsedValue):
                    retentionMonths = parsedValue;
                    return true;
                default:
                    retentionMonths = default;
                    return false;
            }
        }
    }
}
