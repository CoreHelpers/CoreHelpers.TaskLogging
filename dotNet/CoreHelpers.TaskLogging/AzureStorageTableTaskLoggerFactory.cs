using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure.Data.Tables;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

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
            => AnnounceTask(taskType, taskSource, taskWorker, string.Empty, CancellationToken.None);

        public Task<string> AnnounceTask(string taskType, string taskSource, string taskWorker, CancellationToken cancellationToken)
            => AnnounceTask(taskType, taskSource, taskWorker, string.Empty, cancellationToken);

        public Task<string> AnnounceTask(string taskType, string taskSource, string taskWorker, string metaData)
            => AnnounceTask(taskType, taskSource, taskWorker, metaData, CancellationToken.None);

        public async Task<string> AnnounceTask(string taskType, string taskSource, string taskWorker, string metaData, CancellationToken cancellationToken)
        {
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
                TaskData = String.IsNullOrEmpty(metaData) ? string.Empty : metaData
            };

            // get the table name
            var tableName = GetTaskTable();

            // add the entity
            await AddEntityToTable<AzureTableTaskEntity>(tableName, taskEntity, cancellationToken);

            // done
            return taskKey;
        }

        public Task<string> AnnounceTask(string taskType, string taskSource, string taskWorker, IDictionary<string, string> metaDataTyped)
            => AnnounceTask(taskType, taskSource, taskWorker, metaDataTyped, CancellationToken.None);

        public Task<string> AnnounceTask(string taskType, string taskSource, string taskWorker, IDictionary<string, string> metaDataTyped, CancellationToken cancellationToken)
            => AnnounceTask(taskType, taskSource, taskWorker, JsonConvert.SerializeObject(metaDataTyped), cancellationToken);
        
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
            var tableName = GetTaskTable();

            // update the entity
            await UpdateEntityInTable<AzureTableTaskEntity>(tableName, taskEntity);

            // handle the running status
            if (taskStatus == TaskStatus.Running)
                await AddEntityToTable<AzureTableTaskEntity>(GetRunningTaskTable(), taskEntity);
            else if (taskStatus == TaskStatus.Succeed)
                await DeleteEntityByKeys(GetRunningTaskTable(), taskKey, taskKey);
            else if  (taskStatus == TaskStatus.Failed)
            {
                // store the task in the poisioned table
                await AddEntityToTable<AzureTableTaskEntity>(GetFailedTaskTable(), taskEntity);
                
                // remove the task from running table
                await DeleteEntityByKeys(GetRunningTaskTable(), taskKey, taskKey);
            }
        }

        public async Task UpdateTaskWorker(string taskId, string taskWorker)
        {
            // build the task entity
            var taskEntity = new AzureTableTaskEntity()
            {
                PartitionKey = taskId,
                RowKey = taskId, 
                TaskWorker = taskWorker
            };
            
            // get the table name
            var tableName = GetTaskTable();

            // update the entity
            await UpdateEntityInTable<AzureTableTaskEntity>(tableName, taskEntity);
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
            var tableName = GetTaskMessagesTable();

            // get the table client
            var tableClient = _tableServiceClient.GetTableClient(tableName: tableName);

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
            await ExecuteTableOperation(cancellationToken => tableClient.SubmitTransactionAsync(addEntitiesBatch, cancellationToken), cancellationToken => tableClient.CreateIfNotExistsAsync(cancellationToken), CancellationToken.None);
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

        private string GetTaskTable()
            => GetTableName("Tasks");
        
        private string GetExternalTaskIdLookupTable()
            => GetTableName("TasksExternalIdLookup");

        private string GetRunningTaskTable()
            => $"{_environmentPrefix}TasksRunning";

        private string GetFailedTaskTable()
            => GetTableName("TasksFailed");

        private string GetTaskMessagesTable()
            => GetTableName("Messages");

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

        private async Task AddEntityToTable<T>(string tableName, T entity, CancellationToken cancellationToken = default) where T : ITableEntity
            => await ExecuteEntityToTableOperation(tableName, (TableClient tc, CancellationToken token) => tc.AddEntityAsync<T>(entity, token), cancellationToken);

        private async Task UpdateEntityInTable<T>(string tableName, T entity, CancellationToken cancellationToken = default) where T : ITableEntity
            => await ExecuteEntityToTableOperation(tableName, (TableClient tc, CancellationToken token) => tc.UpdateEntityAsync<T>(entity, Azure.ETag.All, cancellationToken: token), cancellationToken);

        private async Task DeleteEntityByKeys(string tableName, string pKey, string rowKey, CancellationToken cancellationToken = default)
            => await ExecuteEntityToTableOperation(tableName, (TableClient tc, CancellationToken token) => tc.DeleteEntityAsync(pKey, rowKey, Azure.ETag.All, token), cancellationToken);

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