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

        public async Task<string> AnnounceTask(string taskType, string taskSource, string taskWorker)
            => await AnnounceTask(taskType, taskSource, taskWorker, string.Empty);

        public async Task<string> AnnounceTask(string taskType, string taskSource, string taskWorker, string metaData)
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
            await AddEntityToTable<AzureTableTaskEntity>(tableName, taskEntity);

            // done
            return taskKey;
        }

        public Task<string> AnnounceTask(string taskType, string taskSource, string taskWorker, IDictionary<string, string> metaDataTyped)
            => AnnounceTask(taskType, taskSource, taskWorker, JsonConvert.SerializeObject(metaDataTyped));
        
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

        public ITaskLogger CreateTaskLogger(string taskKey)
        {
            return new AzureStorageTableTaskLogger(taskKey, _cacheLimit, _cacheTimespan, this);
        }

        public async Task Flush(DateTimeOffset flushTime, string taskKey, IEnumerable<string> messages)
        {
            // check if we have something to flush
            if (messages.Count() == 0)
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
                }));

            // create the entry
            try
            {
                await tableClient.SubmitTransactionAsync(addEntitiesBatch);                
            }
            catch (Azure.RequestFailedException e)
            {
                if (e.ErrorCode != null && e.ErrorCode.Equals("TableNotFound"))
                {
                    await tableClient.CreateAsync();
                    await tableClient.SubmitTransactionAsync(addEntitiesBatch);
                }
            }            
        }

        private string GetTablePrefix()
            => $"{_environmentPrefix}{DateTimeOffset.UtcNow.ToString("yyyyMM")}";        

        private string GetTableName(string tableName)
            => $"{GetTablePrefix()}{tableName}";        

        private string GetTaskTable()
            => GetTableName("Tasks");

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

        private async Task AddEntityToTable<T>(string tableName, T entity) where T : ITableEntity
            => await ExecuteEntityToTableOperation(tableName, (TableClient tc) => tc.AddEntityAsync<T>(entity));

        private async Task UpdateEntityInTable<T>(string tableName, T entity) where T : ITableEntity
            => await ExecuteEntityToTableOperation(tableName, (TableClient tc) => tc.UpdateEntityAsync<T>(entity, Azure.ETag.All));

        private async Task DeleteEntityByKeys(string tableName, string pKey, string rowKey)
            => await ExecuteEntityToTableOperation(tableName, (TableClient tc) => tc.DeleteEntityAsync(pKey, rowKey, Azure.ETag.All));

        private async Task ExecuteEntityToTableOperation(string tableName, Func<TableClient, Task> operation)
        {
            // get the table client
            var tableClient = _tableServiceClient.GetTableClient(tableName: tableName);

            // process the entry
            try
            {                
                await operation(tableClient);                
            }
            catch (Azure.RequestFailedException e)
            {
                if (e.ErrorCode != null && e.ErrorCode.Equals("TableNotFound"))
                {
                    await tableClient.CreateAsync();
                    await operation(tableClient);
                }
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

