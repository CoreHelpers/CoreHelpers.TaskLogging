using Azure;
using Azure.Data.Tables;
using System.Text.Json.Nodes;
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
    public async Task AnnounceTask_PreservesStructuredJsonMetadata()
    {
        var factory = new CapturingAzureStorageTableTaskLoggerFactory();

        await factory.AnnounceTask("type", "source", "worker", new JsonObject
        {
            ["tenant"] = "north",
            ["enabled"] = true,
            ["pageSize"] = 100000,
            ["options"] = new JsonObject { ["mode"] = "full" }
        });

        var metadata = JsonNode.Parse(factory.LastAddedTask!.TaskData)!.AsObject();
        Assert.Equal("north", metadata["tenant"]!.GetValue<string>());
        Assert.True(metadata["enabled"]!.GetValue<bool>());
        Assert.Equal(100000, metadata["pageSize"]!.GetValue<int>());
        Assert.Equal("full", metadata["options"]!["mode"]!.GetValue<string>());
    }

    [Fact]
    public async Task AnnounceTask_SerializesEmptyMetadataAsJsonObject()
    {
        var factory = new CapturingAzureStorageTableTaskLoggerFactory();

        await factory.AnnounceTask("type", "source", "worker");

        Assert.Equal("{}", factory.LastAddedTask!.TaskData);
    }

    [Fact]
    public async Task AnnounceTask_PropagatesCancellationTokenToAzureTableWrite()
    {
        var factory = new CapturingAzureStorageTableTaskLoggerFactory();
        using var cancellationTokenSource = new CancellationTokenSource();

        await factory.AnnounceTask("type", "source", "worker", cancellationTokenSource.Token);

        Assert.Equal(cancellationTokenSource.Token, factory.LastAddCancellationToken);
    }

    [Fact]
    public async Task MergeTaskMetadata_IsPersistedWithNextStatusUpdateAndOverwritesExistingKeys()
    {
        var factory = new InMemoryAzureStorageTableTaskLoggerFactory(new JsonObject { ["existing"] = true, ["state"] = "old", ["nested"] = new JsonObject { ["count"] = 2 } });

        await factory.MergeTaskMetadata(factory.TaskId, new JsonObject { ["state"] = "new", ["added"] = 42 });

        Assert.Equal(0, factory.UpdateCount);

        await factory.UpdateTaskStatus(factory.TaskId, TaskStatus.Running);

        Assert.True(factory.Metadata["existing"]!.GetValue<bool>());
        Assert.Equal("new", factory.Metadata["state"]!.GetValue<string>());
        Assert.Equal(42, factory.Metadata["added"]!.GetValue<int>());
        Assert.Equal(2, factory.Metadata["nested"]!["count"]!.GetValue<int>());
        Assert.Equal(TaskStatus.Running.ToString(), factory.TaskState);
    }

    [Fact]
    public async Task MergeTaskMetadata_WithEmptyMetadata_DoesNotChangeStoredMetadata()
    {
        var factory = new InMemoryAzureStorageTableTaskLoggerFactory(new JsonObject { ["existing"] = "value" });

        await factory.MergeTaskMetadata(factory.TaskId, new JsonObject());
        await factory.UpdateTaskStatus(factory.TaskId, TaskStatus.Pending);

        Assert.True(JsonNode.DeepEquals(new JsonObject { ["existing"] = "value" }, factory.Metadata));
    }

    [Fact]
    public async Task UpdateTaskWorker_UsesAnEtagProtectedPartialUpdate()
    {
        var factory = new CapturingAzureStorageTableTaskLoggerFactory();
        var taskKey = AzureTableTimebasedKeyBuilder.BuildDateTimeBasedRowKey(DateTimeOffset.UtcNow, Guid.NewGuid().ToString());

        await factory.UpdateTaskWorker(taskKey, "new-worker");

        Assert.Equal(new ETag("\"1\""), factory.LastUpdateEtag);
        Assert.Equal("new-worker", factory.LastUpdatedEntity![nameof(AzureTableTaskEntity.TaskWorker)]);
        Assert.False(factory.LastUpdatedEntity.ContainsKey(nameof(AzureTableTaskEntity.TaskState)));
        Assert.False(factory.LastUpdatedEntity.ContainsKey(nameof(AzureTableTaskEntity.TaskData)));
    }

    [Fact]
    public async Task ParallelMetadataUpdates_DoNotLetAnOlderSnapshotOverwriteNewerValues()
    {
        var factory = new InMemoryAzureStorageTableTaskLoggerFactory(new JsonObject(), blockFirstUpdate: true);
        await factory.MergeTaskMetadata(factory.TaskId, new JsonObject { ["state"] = "old", ["first"] = "value" });
        var firstUpdate = factory.UpdateTaskStatus(factory.TaskId, TaskStatus.Pending);
        await factory.FirstUpdateWaiting;

        await factory.MergeTaskMetadata(factory.TaskId, new JsonObject { ["state"] = "new", ["second"] = "value" });
        await factory.UpdateTaskStatus(factory.TaskId, TaskStatus.Pending);
        factory.ReleaseFirstUpdate();
        await firstUpdate;

        Assert.Equal("new", factory.Metadata["state"]!.GetValue<string>());
        Assert.Equal("value", factory.Metadata["first"]!.GetValue<string>());
        Assert.Equal("value", factory.Metadata["second"]!.GetValue<string>());
        Assert.Equal(1, factory.ConflictCount);
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

        public AzureTableTaskEntity? LastAddedTask { get; private set; }
        public CancellationToken LastAddCancellationToken { get; private set; }
        public TableEntity? LastUpdatedEntity { get; private set; }
        public ETag? LastUpdateEtag { get; private set; }

        protected override Task AddEntityToTable<T>(string tableName, T entity, CancellationToken cancellationToken = default)
        {
            Operations.Add(("Add", tableName));
            LastAddedTask = entity as AzureTableTaskEntity ?? LastAddedTask;
            LastAddCancellationToken = cancellationToken;
            return Task.CompletedTask;
        }

        protected override Task<AzureTableTaskEntity> GetTaskEntityFromTable(string tableName, string partitionKey, string rowKey, CancellationToken cancellationToken = default)
            => Task.FromResult(new AzureTableTaskEntity { PartitionKey = partitionKey, RowKey = rowKey, ETag = new ETag("\"1\""), TaskData = "{}" });

        protected override Task UpdateTaskEntityPropertiesInTable(string tableName, TableEntity entity, ETag etag, CancellationToken cancellationToken = default)
        {
            Operations.Add(("Update", tableName));
            LastUpdatedEntity = entity;
            LastUpdateEtag = etag;
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

    private sealed class InMemoryAzureStorageTableTaskLoggerFactory : AzureStorageTableTaskLoggerFactory
    {
        private readonly object _syncRoot = new();
        private readonly TaskCompletionSource<bool>? _continueFirstUpdate;
        private readonly TaskCompletionSource<bool>? _firstUpdateWaiting;
        private AzureTableTaskEntity _entity;
        private int _etagVersion = 1;
        private int _updateAttemptCount;

        public InMemoryAzureStorageTableTaskLoggerFactory(JsonObject metadata, bool blockFirstUpdate = false)
            : base("UseDevelopmentStorage=true", "Dev", 100, TimeSpan.FromMinutes(5))
        {
            if (blockFirstUpdate)
            {
                _continueFirstUpdate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _firstUpdateWaiting = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            TaskId = AzureTableTimebasedKeyBuilder.BuildDateTimeBasedRowKey(DateTimeOffset.UtcNow, Guid.NewGuid().ToString());
            _entity = new AzureTableTaskEntity
            {
                PartitionKey = TaskId,
                RowKey = TaskId,
                ETag = BuildEtag(),
                TaskState = TaskStatus.Pending.ToString(),
                TaskData = metadata.ToJsonString()
            };
        }

        public string TaskId { get; }
        public int ConflictCount { get; private set; }
        public int UpdateCount { get; private set; }
        public Task FirstUpdateWaiting => _firstUpdateWaiting?.Task ?? Task.CompletedTask;

        public void ReleaseFirstUpdate()
            => _continueFirstUpdate?.SetResult(true);

        public JsonObject Metadata
        {
            get
            {
                lock (_syncRoot)
                    return JsonNode.Parse(_entity.TaskData)!.AsObject();
            }
        }

        public string TaskState
        {
            get
            {
                lock (_syncRoot)
                    return _entity.TaskState;
            }
        }

        protected override async Task<AzureTableTaskEntity> GetTaskEntityFromTable(string tableName, string partitionKey, string rowKey, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            lock (_syncRoot)
            {
                return new AzureTableTaskEntity
                {
                    PartitionKey = _entity.PartitionKey,
                    RowKey = _entity.RowKey,
                    ETag = _entity.ETag,
                    TaskState = _entity.TaskState,
                    TaskData = _entity.TaskData
                };
            }
        }

        protected override async Task UpdateTaskEntityPropertiesInTable(string tableName, TableEntity entity, ETag etag, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _updateAttemptCount) == 1 && _firstUpdateWaiting != null && _continueFirstUpdate != null)
            {
                _firstUpdateWaiting.SetResult(true);
                await _continueFirstUpdate.Task;
            }

            await Task.Yield();
            lock (_syncRoot)
            {
                if (etag != _entity.ETag)
                {
                    ConflictCount++;
                    throw new RequestFailedException(412, "ETag mismatch", "UpdateConditionNotSatisfied", null);
                }

                if (entity.TryGetValue(nameof(AzureTableTaskEntity.TaskState), out var taskState))
                    _entity.TaskState = (string)taskState;
                if (entity.TryGetValue(nameof(AzureTableTaskEntity.TaskData), out var taskData))
                    _entity.TaskData = (string)taskData;
                _etagVersion++;
                _entity.ETag = BuildEtag();
                UpdateCount++;
            }
        }

        protected override Task AddEntityToTable<T>(string tableName, T entity, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        private ETag BuildEtag()
            => new ETag($"\"{_etagVersion}\"");
    }
}