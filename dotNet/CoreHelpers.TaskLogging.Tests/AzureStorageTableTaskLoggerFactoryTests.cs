using Azure;
using Azure.Data.Tables;
using Xunit;

namespace CoreHelpers.TaskLogging.Tests;

public sealed class AzureStorageTableTaskLoggerFactoryTests
{
    [Theory]
    [InlineData("2026-01-31T23:59:59.9999999Z", 2026, 1)]
    [InlineData("2026-02-01T00:00:00Z", 2026, 2)]
    public void TaskKeyReferenceTime_PreservesAnnounceMonth(string timestamp, int expectedYear, int expectedMonth)
    {
        var taskKey = AzureTableTimebasedKeyBuilder.BuildDateTimeBasedRowKey(DateTimeOffset.Parse(timestamp), Guid.NewGuid().ToString());

        var referenceTime = AzureTableTimebasedKeyBuilder.GetReferenceTime(taskKey);

        Assert.Equal(expectedYear, referenceTime.Year);
        Assert.Equal(expectedMonth, referenceTime.Month);
    }

    [Fact]
    public void TaskKeyReferenceTime_RejectsInvalidTaskKey()
    {
        var exception = Assert.Throws<ArgumentException>(() => AzureTableTimebasedKeyBuilder.GetReferenceTime("invalid-task-key"));

        Assert.Equal("taskKey", exception.ParamName);
    }

    [Fact]
    public void TimePartitionedTableNames_UseTaskKeyMonth()
    {
        var januaryTaskKey = AzureTableTimebasedKeyBuilder.BuildDateTimeBasedRowKey(new DateTimeOffset(2026, 1, 31, 23, 59, 59, TimeSpan.Zero), Guid.NewGuid().ToString());
        var februaryTaskKey = AzureTableTimebasedKeyBuilder.BuildDateTimeBasedRowKey(new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero), Guid.NewGuid().ToString());

        Assert.Equal("Dev202601Tasks", AzureStorageTableTaskLoggerFactory.GetTimePartitionedTableName("Dev", "Tasks", januaryTaskKey));
        Assert.Equal("Dev202601Messages", AzureStorageTableTaskLoggerFactory.GetTimePartitionedTableName("Dev", "Messages", januaryTaskKey));
        Assert.Equal("Dev202602Tasks", AzureStorageTableTaskLoggerFactory.GetTimePartitionedTableName("Dev", "Tasks", februaryTaskKey));
        Assert.Equal("Dev202602Messages", AzureStorageTableTaskLoggerFactory.GetTimePartitionedTableName("Dev", "Messages", februaryTaskKey));
    }

    [Fact]
    public async Task TaskWorkflows_KeepUsingAnnounceMonth()
    {
        var taskKey = AzureTableTimebasedKeyBuilder.BuildDateTimeBasedRowKey(new DateTimeOffset(2026, 1, 31, 23, 59, 59, TimeSpan.Zero), Guid.NewGuid().ToString());
        var factory = new CapturingAzureStorageTableTaskLoggerFactory();

        await factory.UpdateTaskStatus(taskKey, TaskStatus.Running);
        await factory.UpdateTaskWorker(taskKey, "worker");
        await factory.UpdateTaskStatus(taskKey, TaskStatus.Failed);
        await factory.MergePendingMessages(DateTimeOffset.UtcNow, taskKey, new[] { "message" });

        Assert.Equal(
            new[]
            {
                ("Update", "Dev202601Tasks"),
                ("Add", "DevTasksRunning"),
                ("Update", "Dev202601Tasks"),
                ("Update", "Dev202601Tasks"),
                ("Add", "Dev202601TasksFailed"),
                ("Delete", "DevTasksRunning"),
                ("Transaction", "Dev202601Messages")
            },
            factory.Operations);
    }

    [Fact]
    public async Task AnnounceTask_UsesGeneratedTaskKeyMonth()
    {
        var factory = new CapturingAzureStorageTableTaskLoggerFactory();

        var taskKey = await factory.AnnounceTask("type", "source", "worker");

        Assert.Equal(
            new[] { ("Add", AzureStorageTableTaskLoggerFactory.GetTimePartitionedTableName("Dev", "Tasks", taskKey)) },
            factory.Operations);
    }

    [Fact]
    public void GetNextMessageBatchSize_LimitsBatchToOneHundredEntities()
    {
        var messages = Enumerable.Range(0, 101).Select(index => $"message-{index}").ToArray();

        var batchSize = AzureStorageTableTaskLoggerFactory.GetNextMessageBatchSize("task", messages);

        Assert.Equal(AzureStorageTableTaskLoggerFactory.MaxTransactionEntityCount, batchSize);
    }

    [Fact]
    public void GetNextMessageBatchSize_LimitsBatchToFourMiB()
    {
        var message = new string('"', 350_000);
        var messages = Enumerable.Repeat(message, 10).ToArray();

        var batchSize = AzureStorageTableTaskLoggerFactory.GetNextMessageBatchSize("task", messages);
        var batchSizeInBytes = 4 * 1024 + messages.Take(batchSize).Sum(item => AzureStorageTableTaskLoggerFactory.GetMessageEntitySize("task", item));
        var nextEntitySize = AzureStorageTableTaskLoggerFactory.GetMessageEntitySize("task", messages[batchSize]);

        Assert.InRange(batchSizeInBytes, 1, AzureStorageTableTaskLoggerFactory.MaxTransactionSizeInBytes);
        Assert.True(batchSizeInBytes + nextEntitySize > AzureStorageTableTaskLoggerFactory.MaxTransactionSizeInBytes);
    }

    [Fact]
    public async Task ExecuteTableOperation_WhenTableIsMissing_CreatesTableAndRetriesWithCancellationToken()
    {
        var operationCalls = 0;
        var createTableCalls = 0;
        using var cancellationTokenSource = new CancellationTokenSource();
        var receivedTokens = new List<CancellationToken>();

        await AzureStorageTableTaskLoggerFactory.ExecuteTableOperation(
            cancellationToken =>
            {
                receivedTokens.Add(cancellationToken);
                operationCalls++;
                return operationCalls == 1
                    ? Task.FromException(new RequestFailedException(404, "Missing table", "TableNotFound", null))
                    : Task.CompletedTask;
            },
            cancellationToken =>
            {
                receivedTokens.Add(cancellationToken);
                createTableCalls++;
                return Task.CompletedTask;
            },
            cancellationTokenSource.Token);

        Assert.Equal(2, operationCalls);
        Assert.Equal(1, createTableCalls);
        Assert.All(receivedTokens, cancellationToken => Assert.Equal(cancellationTokenSource.Token, cancellationToken));
    }

    [Fact]
    public async Task ExecuteTableOperation_WhenStorageFails_RethrowsException()
    {
        var storageException = new RequestFailedException(503, "Storage unavailable", "ServerBusy", null);

        var thrownException = await Assert.ThrowsAsync<RequestFailedException>(() => AzureStorageTableTaskLoggerFactory.ExecuteTableOperation(
            cancellationToken => Task.FromException(storageException),
            cancellationToken => Task.CompletedTask,
            CancellationToken.None));

        Assert.Same(storageException, thrownException);
    }

    [Fact]
    public async Task ExecuteTableOperation_WhenRetryFails_RethrowsRetryException()
    {
        var operationCalls = 0;
        var retryException = new RequestFailedException(503, "Storage unavailable", "ServerBusy", null);

        var thrownException = await Assert.ThrowsAsync<RequestFailedException>(() => AzureStorageTableTaskLoggerFactory.ExecuteTableOperation(
            cancellationToken =>
            {
                operationCalls++;
                return operationCalls == 1
                    ? Task.FromException(new RequestFailedException(404, "Missing table", "TableNotFound", null))
                    : Task.FromException(retryException);
            },
            cancellationToken => Task.CompletedTask,
            CancellationToken.None));

        Assert.Same(retryException, thrownException);
        Assert.Equal(2, operationCalls);
    }

    private sealed class CapturingAzureStorageTableTaskLoggerFactory : AzureStorageTableTaskLoggerFactory
    {
        public CapturingAzureStorageTableTaskLoggerFactory()
            : base("UseDevelopmentStorage=true", "Dev", 100, TimeSpan.FromMinutes(5))
        {
        }

        public List<(string Operation, string TableName)> Operations { get; } = new();

        protected override Task AddEntityToTable<T>(string tableName, T entity, CancellationToken cancellationToken = default)
        {
            Operations.Add(("Add", tableName));
            return Task.CompletedTask;
        }

        protected override Task UpdateEntityInTable<T>(string tableName, T entity, CancellationToken cancellationToken = default)
        {
            Operations.Add(("Update", tableName));
            return Task.CompletedTask;
        }

        protected override Task DeleteEntityByKeys(string tableName, string pKey, string rowKey, CancellationToken cancellationToken = default)
        {
            Operations.Add(("Delete", tableName));
            return Task.CompletedTask;
        }

        protected override Task SubmitTransactionToTable(string tableName, IReadOnlyList<TableTransactionAction> actions, CancellationToken cancellationToken)
        {
            Operations.Add(("Transaction", tableName));
            return Task.CompletedTask;
        }
    }
}