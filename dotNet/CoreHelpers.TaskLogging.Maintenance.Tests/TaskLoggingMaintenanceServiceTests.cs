using Azure.Data.Tables;
using CoreHelpers.TaskLogging.Maintenance;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CoreHelpers.TaskLogging.Maintenance.Tests;

public class TaskLoggingMaintenanceServiceTests
{
    [Fact]
    public void AddTaskLoggingMaintenance_RegistersMaintenanceService()
    {
        var services = new ServiceCollection();

        services.AddTaskLoggingMaintenance();

        using var provider = services.BuildServiceProvider();
        var maintenanceService = provider.GetRequiredService<ITaskLoggingMaintenanceService>();

        Assert.IsType<TaskLoggingMaintenanceService>(maintenanceService);
    }

    [Fact]
    public async Task CleanupAsync_MatchesOnlyRotatableTaskLoggerTables()
    {
        var storage = CreateStorage(
            new[]
            {
                "tlprod202506Tasks",
                "tlprod202506Messages",
                "tlprod202506TasksFailed",
                "tlprod202506TasksExternalIdLookup",
                "tlprodTasksRunning",
                "tlprodSettings",
                "clprod202506Tasks",
                "tlprod202513Tasks",
                "tlprod202506Unexpected"
            },
            CreateSettings(6));
        var service = CreateService(storage);

        var result = await service.CleanupAsync(CreateOptions(new DateTimeOffset(2026, 6, 7, 0, 0, 0, TimeSpan.Zero)));

        Assert.Equal(
            new[]
            {
                "tlprod202506Messages",
                "tlprod202506Tasks",
                "tlprod202506TasksExternalIdLookup",
                "tlprod202506TasksFailed"
            },
            result.MatchingTables);
    }

    [Fact]
    public async Task CleanupAsync_WithSixMonthRetention_DeletesTablesOlderThanCutoff()
    {
        var storage = CreateStorage(
            new[]
            {
                "tlprod202511Tasks",
                "tlprod202512Tasks",
                "tlprod202601Tasks",
                "tlprod202606Tasks"
            },
            CreateSettings(6));
        var service = CreateService(storage);

        var result = await service.CleanupAsync(CreateOptions(
            new DateTimeOffset(2026, 6, 7, 0, 0, 0, TimeSpan.Zero),
            dryRun: false));

        Assert.True(result.RotationConfigured);
        Assert.Equal(6, result.LogRetentionMonths);
        Assert.Equal(new[] { "tlprod202511Tasks" }, result.DeletedTables);
        Assert.Equal(result.DeletedTables, storage.DeletedTables);
        Assert.Contains("tlprod202512Tasks", result.SkippedTables);
        Assert.Contains("tlprod202601Tasks", result.SkippedTables);
        Assert.Contains("tlprod202606Tasks", result.SkippedTables);
    }

    [Fact]
    public async Task CleanupAsync_WithThreeMonthRetention_DeletesTablesOlderThanCutoff()
    {
        var storage = CreateStorage(
            new[]
            {
                "tlprod202602Tasks",
                "tlprod202603Tasks",
                "tlprod202604Tasks",
                "tlprod202606Tasks"
            },
            CreateSettings(3));
        var service = CreateService(storage);

        var result = await service.CleanupAsync(CreateOptions(
            new DateTimeOffset(2026, 6, 7, 0, 0, 0, TimeSpan.Zero),
            dryRun: false));

        Assert.Equal(new[] { "tlprod202602Tasks" }, result.DeletedTables);
        Assert.Contains("tlprod202603Tasks", result.SkippedTables);
        Assert.Contains("tlprod202604Tasks", result.SkippedTables);
        Assert.Contains("tlprod202606Tasks", result.SkippedTables);
    }

    [Fact]
    public async Task CleanupAsync_NeverDeletesCurrentMonth()
    {
        var storage = CreateStorage(new[] { "tlprod202606Tasks" }, CreateSettings(1));
        var service = CreateService(storage);

        var result = await service.CleanupAsync(CreateOptions(
            new DateTimeOffset(2026, 6, 7, 0, 0, 0, TimeSpan.Zero),
            dryRun: false));

        Assert.Empty(result.DeletedTables);
        Assert.Empty(storage.DeletedTables);
        Assert.Equal(new[] { "tlprod202606Tasks" }, result.SkippedTables);
    }

    [Fact]
    public async Task CleanupAsync_WithMissingSettings_DoesNotRotate()
    {
        var storage = CreateStorage(new[] { "tlprod202501Tasks" }, null);
        var service = CreateService(storage);

        var result = await service.CleanupAsync(CreateOptions(
            new DateTimeOffset(2026, 6, 7, 0, 0, 0, TimeSpan.Zero),
            dryRun: false));

        Assert.False(result.RotationConfigured);
        Assert.Null(result.LogRetentionMonths);
        Assert.Empty(result.DeletedTables);
        Assert.Empty(storage.DeletedTables);
        Assert.Equal(new[] { "tlprod202501Tasks" }, result.SkippedTables);
    }

    [Fact]
    public async Task CleanupAsync_WithMissingRetentionValue_DoesNotRotate()
    {
        var storage = CreateStorage(new[] { "tlprod202501Tasks" }, CreateSettingsWithoutRetention());
        var service = CreateService(storage);

        var result = await service.CleanupAsync(CreateOptions(
            new DateTimeOffset(2026, 6, 7, 0, 0, 0, TimeSpan.Zero),
            dryRun: false));

        Assert.False(result.RotationConfigured);
        Assert.Null(result.LogRetentionMonths);
        Assert.Empty(storage.DeletedTables);
    }

    [Fact]
    public async Task CleanupAsync_WithInvalidRetentionValue_DoesNotRotate()
    {
        var storage = CreateStorage(new[] { "tlprod202501Tasks" }, CreateSettings(0));
        var service = CreateService(storage);

        var result = await service.CleanupAsync(CreateOptions(
            new DateTimeOffset(2026, 6, 7, 0, 0, 0, TimeSpan.Zero),
            dryRun: false));

        Assert.False(result.RotationConfigured);
        Assert.Equal(0, result.LogRetentionMonths);
        Assert.Empty(storage.DeletedTables);
    }

    [Fact]
    public async Task CleanupAsync_WithDryRun_DoesNotDeleteCandidates()
    {
        var storage = CreateStorage(new[] { "tlprod202501Tasks" }, CreateSettings(6));
        var service = CreateService(storage);

        var result = await service.CleanupAsync(CreateOptions(
            new DateTimeOffset(2026, 6, 7, 0, 0, 0, TimeSpan.Zero),
            dryRun: true));

        Assert.True(result.RotationConfigured);
        Assert.Equal(new[] { "tlprod202501Tasks" }, result.DeletedTables);
        Assert.Empty(storage.DeletedTables);
        Assert.Empty(result.SkippedTables);
    }

    private static TaskLoggingCleanupOptions CreateOptions(DateTimeOffset referenceDateUtc, bool dryRun = true)
        => new()
        {
            ConnectionString = "UseDevelopmentStorage=true",
            TaskLoggerPrefix = "tlprod",
            ReferenceDateUtc = referenceDateUtc,
            DryRun = dryRun
        };

    private static TaskLoggingMaintenanceService CreateService(FakeTaskLoggingTableStorage storage)
        => new(_ => storage);

    private static FakeTaskLoggingTableStorage CreateStorage(IReadOnlyList<string> tableNames, TableEntity? settings)
        => new(tableNames, settings);

    private static TableEntity CreateSettings(int retentionMonths)
    {
        var entity = CreateSettingsWithoutRetention();
        entity[TaskLoggingMaintenanceConstants.LogRetentionMonthsProperty] = retentionMonths;
        return entity;
    }

    private static TableEntity CreateSettingsWithoutRetention()
        => new(
            TaskLoggingMaintenanceConstants.SettingsPartitionKey,
            TaskLoggingMaintenanceConstants.SettingsRowKey);
}
