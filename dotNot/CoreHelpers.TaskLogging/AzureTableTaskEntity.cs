using System;
using System.Collections.Generic;
using Azure;
using Azure.Data.Tables;

namespace CoreHelpers.TaskLogging
{
	internal class AzureTableTaskEntity : ITableEntity
    {
        public string PartitionKey { get; set; } = default!;
        public string RowKey { get; set; } = default!;
        public ETag ETag { get; set; } = default!;
        public DateTimeOffset? Timestamp { get; set; } = default!;

        public string TaskState { get; set; } = TaskStatus.Pending.ToString();

        public DateTimeOffset? TaskStartDate = null;
        public DateTimeOffset? TaskEndDate = null;

        public string TaskType { get; set; } = default!;
        public string TaskSource { get; set; } = default!;
        public string TaskWorker { get; set; } = default!;

        public string TaskData { get; set; } = default!;
    }
}

