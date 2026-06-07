using Azure.Data.Tables;
using CoreHelpers.TaskLogging.Maintenance;

namespace CoreHelpers.TaskLogging.Maintenance.Tests;

internal class FakeTaskLoggingTableStorage : ITaskLoggingTableStorage
{
    private readonly IReadOnlyList<string> _tableNames;
    private readonly TableEntity? _settingsEntity;

    public FakeTaskLoggingTableStorage(IReadOnlyList<string> tableNames, TableEntity? settingsEntity)
    {
        _tableNames = tableNames;
        _settingsEntity = settingsEntity;
    }

    public List<string> DeletedTables { get; } = new();

    public Task<IReadOnlyList<string>> ListTableNamesAsync(CancellationToken cancellationToken)
        => Task.FromResult(_tableNames);

    public Task<TableEntity?> GetSettingsEntityAsync(
        string tableName,
        string partitionKey,
        string rowKey,
        CancellationToken cancellationToken)
    {
        if (tableName != "tlprodSettings" ||
            partitionKey != TaskLoggingMaintenanceConstants.SettingsPartitionKey ||
            rowKey != TaskLoggingMaintenanceConstants.SettingsRowKey)
            return Task.FromResult<TableEntity?>(null);

        return Task.FromResult(_settingsEntity);
    }

    public Task DeleteTableAsync(string tableName, CancellationToken cancellationToken)
    {
        DeletedTables.Add(tableName);
        return Task.CompletedTask;
    }
}
