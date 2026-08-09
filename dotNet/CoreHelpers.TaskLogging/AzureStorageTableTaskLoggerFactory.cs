using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Azure.Data.Tables;
using Microsoft.Extensions.DependencyInjection;

namespace CoreHelpers.TaskLogging
{
    internal class AzureStorageTableTaskLoggerFactory : ITaskLoggerFactory
    {
        internal const int MaxTransactionEntityCount = 100;
        internal const int MaxTransactionSizeInBytes = 4 * 1024 * 1024;
        private const int TransactionEnvelopeSizeInBytes = 4 * 1024;
        private const int EntityEnvelopeSizeInBytes = 4 * 1024;
        private const int MaxJsonEncodedBytesPerCharacter = 6;
        private readonly int _cacheLimit;
        private readonly TimeSpan _cacheTimespan;
        private readonly string _environmentPrefix;
        private readonly object _pendingMetadataSyncRoot = new object();
        private readonly Dictionary<string, JsonObject> _pendingMetadata = new Dictionary<string, JsonObject>();
        private readonly TableServiceClient _tableServiceClient;
        private long _messageTimeStampCounter = 0;

        public AzureStorageTableTaskLoggerFactory(string connectionString, string environmentPrefix, int cacheLimit, TimeSpan cacheTimespan)
        {            
            _tableServiceClient = new TableServiceClient(connectionString);
            _environmentPrefix = environmentPrefix;
            _cacheLimit = cacheLimit;
            _cacheTimespan = cacheTimespan;
        }

        public Task<string> AnnounceTask(string taskType, string taskSource, string taskWorker)
            => AnnounceTask(taskType, taskSource, taskWorker, new JsonObject(), CancellationToken.None);

        public Task<string> AnnounceTask(string taskType, string taskSource, string taskWorker, CancellationToken cancellationToken)
            => AnnounceTask(taskType, taskSource, taskWorker, new JsonObject(), cancellationToken);

        public Task<string> AnnounceTask(string taskType, string taskSource, string taskWorker, JsonObject metadata)
            => AnnounceTask(taskType, taskSource, taskWorker, metadata, CancellationToken.None);

        public async Task<string> AnnounceTask(string taskType, string taskSource, string taskWorker, JsonObject metadata, CancellationToken cancellationToken)
        {
            if (metadata == null)
                throw new ArgumentNullException(nameof(metadata));

            // define the refDate
            var refDate = DateTime.UtcNow;

            // build the time sorted task key
            var taskKey = AzureTableTimebasedKeyBuilder.BuildDateTimeBasedRowKey(refDate, Guid.NewGuid().ToString());

            // build the task entity
            var taskEntity = new AzureTableTaskEntity()
            {
                PartitionKey = taskKey,
                RowKey = taskKey,
                Timestamp = refDate,                
                TaskState = TaskStatus.Pending.ToString(),
                TaskType = taskType,
                TaskSource = taskSource,
                TaskWorker = taskWorker,
                TaskData = metadata.ToJsonString()
            };

            // get the table name
            var tableName = GetTaskTable(taskKey);

            // add the entity
            await AddEntityToTable<AzureTableTaskEntity>(tableName, taskEntity, cancellationToken);

            // done
            return taskKey;
        }

        public Task MergeTaskMetadata(string taskId, JsonObject metadata)
        {
            if (metadata == null)
                throw new ArgumentNullException(nameof(metadata));

            if (metadata.Count == 0)
                return Task.CompletedTask;

            lock (_pendingMetadataSyncRoot)
            {
                if (!_pendingMetadata.TryGetValue(taskId, out var pendingMetadata))
                {
                    pendingMetadata = new JsonObject();
                    _pendingMetadata[taskId] = pendingMetadata;
                }

                foreach (var property in metadata)
                    pendingMetadata[property.Key] = property.Value?.DeepClone();
            }

            return Task.CompletedTask;
        }
        
        public async Task UpdateTaskStatus(string taskKey, TaskStatus taskStatus)
        {            
            await UpdateTaskStatus(taskKey, taskStatus, string.Empty);
        }

        public async Task UpdateTaskStatus(string taskKey, TaskStatus taskStatus, string taskWorker)
        {
            // build the task entity
            var taskEntity = new AzureTableTaskEntity()
            {
                PartitionKey = taskKey,
                RowKey = taskKey,                
                TaskState = taskStatus.ToString()                
            };
            
            // check the worker
            if (!string.IsNullOrEmpty(taskWorker))
                taskEntity.TaskWorker = taskWorker;

            // adjust the timings
            if (taskStatus == TaskStatus.Running)
                taskEntity.TaskStartDate = DateTimeOffset.UtcNow;
            else if (taskStatus == TaskStatus.Succeed || taskStatus == TaskStatus.Failed)
                taskEntity.TaskEndDate = DateTimeOffset.UtcNow;

            // get the table name
            var tableName = GetTaskTable(taskKey);

            // update the entity
            var persistedMetadata = await UpdateTaskEntityInTable(tableName, taskEntity);
            RemovePersistedMetadata(taskKey, persistedMetadata);

            // handle the running status
            if (taskStatus == TaskStatus.Running)
                await AddEntityToTable<AzureTableTaskEntity>(GetRunningTaskTable(), taskEntity);
            else if (taskStatus == TaskStatus.Succeed)
                await DeleteEntityByKeys(GetRunningTaskTable(), taskKey, taskKey);
            else if  (taskStatus == TaskStatus.Failed)
            {
                // store the task in the poisioned table
                await AddEntityToTable<AzureTableTaskEntity>(GetFailedTaskTable(taskKey), taskEntity);
                
                // remove the task from running table
                await DeleteEntityByKeys(GetRunningTaskTable(), taskKey, taskKey);
            }
        }

        private JsonObject GetPendingMetadataSnapshot(string taskId)
        {
            lock (_pendingMetadataSyncRoot)
            {
                return _pendingMetadata.TryGetValue(taskId, out var metadata)
                    ? (JsonObject)metadata.DeepClone()
                    : new JsonObject();
            }
        }

        private void RemovePersistedMetadata(string taskId, JsonObject persistedMetadata)
        {
            if (persistedMetadata.Count == 0)
                return;

            lock (_pendingMetadataSyncRoot)
            {
                if (!_pendingMetadata.TryGetValue(taskId, out var pendingMetadata))
                    return;

                foreach (var property in persistedMetadata)
                {
                    if (pendingMetadata.TryGetPropertyValue(property.Key, out var value) && JsonNode.DeepEquals(value, property.Value))
                        pendingMetadata.Remove(property.Key);
                }

                if (pendingMetadata.Count == 0)
                    _pendingMetadata.Remove(taskId);
            }
        }

        private async Task<JsonObject> UpdateTaskEntityInTable(string tableName, AzureTableTaskEntity taskEntity)
        {
            const int maximumAttempts = 10;

            for (var attempt = 1; attempt <= maximumAttempts; attempt++)
            {
                var pendingMetadata = GetPendingMetadataSnapshot(taskEntity.PartitionKey);
                var currentEntity = await GetTaskEntityFromTable(tableName, taskEntity.PartitionKey, taskEntity.RowKey);
                var updateEntity = new TableEntity(taskEntity.PartitionKey, taskEntity.RowKey)
                {
                    [nameof(AzureTableTaskEntity.TaskState)] = taskEntity.TaskState
                };

                if (!string.IsNullOrEmpty(taskEntity.TaskWorker))
                    updateEntity[nameof(AzureTableTaskEntity.TaskWorker)] = taskEntity.TaskWorker;
                if (taskEntity.TaskStartDate.HasValue)
                    updateEntity[nameof(AzureTableTaskEntity.TaskStartDate)] = taskEntity.TaskStartDate.Value;
                if (taskEntity.TaskEndDate.HasValue)
                    updateEntity[nameof(AzureTableTaskEntity.TaskEndDate)] = taskEntity.TaskEndDate.Value;

                if (pendingMetadata.Count > 0)
                {
                    var metadata = DeserializeMetadata(currentEntity.TaskData);
                    foreach (var property in pendingMetadata)
                        metadata[property.Key] = property.Value?.DeepClone();
                    updateEntity[nameof(AzureTableTaskEntity.TaskData)] = metadata.ToJsonString();
                }

                try
                {
                    await UpdateTaskEntityPropertiesInTable(tableName, updateEntity, currentEntity.ETag);
                    return pendingMetadata;
                }
                catch (Azure.RequestFailedException exception) when (exception.Status == 412)
                {
                    if (attempt == maximumAttempts)
                        throw;
                }
            }

            throw new InvalidOperationException("The task update retry loop completed unexpectedly.");
        }

        private static JsonObject DeserializeMetadata(string? metadataJson)
        {
            if (string.IsNullOrEmpty(metadataJson))
                return new JsonObject();

            return JsonNode.Parse(metadataJson) as JsonObject ?? throw new JsonException("Task metadata must be a JSON object.");
        }

        public async Task UpdateTaskWorker(string taskId, string taskWorker)
        {
            // get the table name
            var tableName = GetTaskTable(taskId);

            const int maximumAttempts = 10;
            for (var attempt = 1; attempt <= maximumAttempts; attempt++)
            {
                var currentEntity = await GetTaskEntityFromTable(tableName, taskId, taskId);
                var updateEntity = new TableEntity(taskId, taskId)
                {
                    [nameof(AzureTableTaskEntity.TaskWorker)] = taskWorker
                };

                try
                {
                    await UpdateTaskEntityPropertiesInTable(tableName, updateEntity, currentEntity.ETag);
                    return;
                }
                catch (Azure.RequestFailedException exception) when (exception.Status == 412)
                {
                    if (attempt == maximumAttempts)
                        throw;
                }
            }
        }

        public Task<string?> LookupTaskIdByExternalId(string externalTaskId)
            => LookupTaskIdByExternalId(externalTaskId, CancellationToken.None);

        public async Task<string?> LookupTaskIdByExternalId(string externalTaskId, CancellationToken cancellationToken)
        {
            // get the table name
            var tableName = GetExternalTaskIdLookupTable();

            // lookup 
            var externalIdEnties = await QueryEntitiyFromTableByPartitionKey<AzureTableTaskEntity>(tableName, externalTaskId, cancellationToken);
            if (externalIdEnties.Length == 0)
                return null;

            return externalIdEnties.First().RowKey;
        }

        public Task RegisterExternlIdForTask(string taskId, string externalTaskId)
            => RegisterExternlIdForTask(taskId, externalTaskId, CancellationToken.None);

        public async Task RegisterExternlIdForTask(string taskId, string externalTaskId, CancellationToken cancellationToken)
        {
            // build the task entity
            var externalIdForTaskEntity = new AzureTableTaskEntity()
            {
                PartitionKey = externalTaskId,
                RowKey = taskId,
            };
            
            // get the table name
            var tableName = GetExternalTaskIdLookupTable();
            
            await AddEntityToTable<AzureTableTaskEntity>(tableName, externalIdForTaskEntity, cancellationToken);
        }

        public async Task<string[]> MergePendingMessagesIfNeeded(DateTimeOffset flushTime, bool force, string taskKey, string[] messages)
        {
            // check if the cache limit is exceeded
            if (messages.Length >= Math.Min(_cacheLimit, MaxTransactionEntityCount) || force)
            {
                var batchSize = GetNextMessageBatchSize(taskKey, messages);
                await MergePendingMessages(flushTime, taskKey, messages.Take(batchSize).ToArray());
                return messages.Skip(batchSize).ToArray();
            }

            // at this point we didn't flush anything
            return messages;
        }
        
        public async Task MergePendingMessages(DateTimeOffset flushTime, string taskKey, string[] messages)
        {
            // check if we have something to flush
            if (!messages.Any())
                return;

            // get the table name
            var tableName = GetTaskMessagesTable(taskKey);

            // build the table transaction            
            var addEntitiesBatch = messages.Select(m => new TableTransactionAction(
                TableTransactionActionType.Add,
                new AzureTableMessageEntity()
                {
                    PartitionKey = taskKey,
                    RowKey = BuildNextLogEntryTimestamp(),
                    Timestamp = flushTime,
                    Message = m
                })).ToArray();

            // create the entry
            await SubmitTransactionToTable(tableName, addEntitiesBatch, CancellationToken.None);
        }

        internal static int GetNextMessageBatchSize(string taskKey, IReadOnlyList<string> messages)
        {
            var batchSize = 0;
            var transactionSize = TransactionEnvelopeSizeInBytes;

            while (batchSize < messages.Count && batchSize < MaxTransactionEntityCount)
            {
                var entitySize = GetMessageEntitySize(taskKey, messages[batchSize]);
                if (batchSize > 0 && transactionSize + entitySize > MaxTransactionSizeInBytes)
                    break;

                if (transactionSize + entitySize > MaxTransactionSizeInBytes)
                    throw new InvalidOperationException("A task log message is too large for an Azure Table transaction.");

                transactionSize += entitySize;
                batchSize++;
            }

            return batchSize;
        }

        internal static int GetMessageEntitySize(string taskKey, string message)
        {
            // A UTF-16 character can occupy up to six bytes when JSON escaped. The
            // fixed allowance covers the other properties and multipart headers.
            return checked((taskKey.Length + message.Length) * MaxJsonEncodedBytesPerCharacter + EntityEnvelopeSizeInBytes);
        }

        private string GetTablePrefix()
            => $"{_environmentPrefix}{DateTimeOffset.UtcNow.ToString("yyyyMM")}";        

        private string GetTableName(string tableName)
            => $"{GetTablePrefix()}{tableName}";        

        private string GetTableName(string tableName, string taskKey)
            => GetTimePartitionedTableName(_environmentPrefix, tableName, taskKey);

        internal static string GetTimePartitionedTableName(string environmentPrefix, string tableName, string taskKey)
            => $"{environmentPrefix}{AzureTableTimebasedKeyBuilder.GetReferenceTime(taskKey):yyyyMM}{tableName}";

        private string GetTaskTable(string taskKey)
            => GetTableName("Tasks", taskKey);
        
        private string GetExternalTaskIdLookupTable()
            => GetTableName("TasksExternalIdLookup");

        private string GetRunningTaskTable()
            => $"{_environmentPrefix}TasksRunning";

        private string GetFailedTaskTable(string taskKey)
            => GetTableName("TasksFailed", taskKey);

        private string GetTaskMessagesTable(string taskKey)
            => GetTableName("Messages", taskKey);

        private string BuildNextLogEntryTimestamp()
        {
            // increase counter 
            var localEntryCounter = Interlocked.Increment(ref _messageTimeStampCounter);

            // define baseline 
            DateTime origin = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);

            // generate the timestamp
            TimeSpan diff = DateTime.Now.ToUniversalTime() - origin;

            // format correctly
            return string.Format("{0}-{1}", Convert.ToInt64(diff.TotalSeconds), localEntryCounter.ToString("00000000"));
        }

        protected virtual async Task AddEntityToTable<T>(string tableName, T entity, CancellationToken cancellationToken = default) where T : ITableEntity
            => await ExecuteEntityToTableOperation(tableName, (TableClient tc, CancellationToken token) => tc.AddEntityAsync<T>(entity, token), cancellationToken);

        protected virtual async Task<AzureTableTaskEntity> GetTaskEntityFromTable(string tableName, string partitionKey, string rowKey, CancellationToken cancellationToken = default)
        {
            AzureTableTaskEntity? result = null;
            await ExecuteEntityToTableOperation(tableName, async (TableClient tableClient, CancellationToken token) =>
            {
                result = (await tableClient.GetEntityAsync<AzureTableTaskEntity>(partitionKey, rowKey, cancellationToken: token)).Value;
            }, cancellationToken);
            return result!;
        }

        protected virtual async Task UpdateTaskEntityPropertiesInTable(string tableName, TableEntity entity, Azure.ETag etag, CancellationToken cancellationToken = default)
            => await ExecuteEntityToTableOperation(tableName, (TableClient tableClient, CancellationToken token) => tableClient.UpdateEntityAsync(entity, etag, TableUpdateMode.Merge, token), cancellationToken);

        protected virtual async Task DeleteEntityByKeys(string tableName, string pKey, string rowKey, CancellationToken cancellationToken = default)
            => await ExecuteEntityToTableOperation(tableName, (TableClient tc, CancellationToken token) => tc.DeleteEntityAsync(pKey, rowKey, Azure.ETag.All, token), cancellationToken);

        protected virtual async Task SubmitTransactionToTable(string tableName, IReadOnlyList<TableTransactionAction> actions, CancellationToken cancellationToken)
        {
            var tableClient = _tableServiceClient.GetTableClient(tableName: tableName);
            await ExecuteTableOperation(token => tableClient.SubmitTransactionAsync(actions, token), token => tableClient.CreateIfNotExistsAsync(token), cancellationToken);
        }

        private async Task<T[]> QueryEntitiyFromTableByPartitionKey<T>(string tableName, string partitionKey, CancellationToken cancellationToken = default) where T : class, ITableEntity
        {
            var result = new List<T>();
            
            await ExecuteEntityToTableOperation(tableName, async (TableClient tc, CancellationToken token) =>
            {
                var entities = tc.QueryAsync<T>(filter: $"PartitionKey eq '{partitionKey}'", cancellationToken: token);
                
                await foreach (var entity in entities.WithCancellation(token))
                    result.Add(entity);
            }, cancellationToken);

            return result.ToArray();
        }

        private async Task ExecuteEntityToTableOperation(string tableName, Func<TableClient, CancellationToken, Task> operation, CancellationToken cancellationToken)
        {
            // get the table client
            var tableClient = _tableServiceClient.GetTableClient(tableName: tableName);

            await ExecuteTableOperation(token => operation(tableClient, token), token => tableClient.CreateIfNotExistsAsync(token), cancellationToken);
        }

        internal static async Task ExecuteTableOperation(Func<CancellationToken, Task> operation, Func<CancellationToken, Task> createTable, CancellationToken cancellationToken)
        {
            try
            {
                await operation(cancellationToken);
            }
            catch (Azure.RequestFailedException e) when (string.Equals(e.ErrorCode, "TableNotFound", StringComparison.Ordinal))
            {
                await createTable(cancellationToken);
                await operation(cancellationToken);
            }
        }
    }

    public static class AzureStorageTableTaskLoggerFactoryServiceCollectionExtension
    {
        public static IServiceCollection AddTaskLoggerForAzureStorageTable(this IServiceCollection services, string connectionString, string environmentPrefix, int lineCacheLimit) 
        {
            services.AddSingleton<ITaskLoggerFactory>(new AzureStorageTableTaskLoggerFactory(connectionString, environmentPrefix, lineCacheLimit, TimeSpan.FromMinutes(5)));
            return services;
        }
        
        public static IServiceCollection AddTaskLoggerForAzureStorageTable(this IServiceCollection services, string connectionString, string environmentPrefix, int lineCacheLimit, TimeSpan cacheTimeSpan) 
        {
            services.AddSingleton<ITaskLoggerFactory>(new AzureStorageTableTaskLoggerFactory(connectionString, environmentPrefix, lineCacheLimit, cacheTimeSpan));
            return services;
        }
    }
}