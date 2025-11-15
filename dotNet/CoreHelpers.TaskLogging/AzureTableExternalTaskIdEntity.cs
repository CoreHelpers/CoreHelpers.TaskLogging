using System;
using Azure;
using Azure.Data.Tables;

namespace CoreHelpers.TaskLogging
{
    public class AzureTableExternalTaskIdEntity : ITableEntity
    {
        public string PartitionKey { get; set; } = default!;
        public string RowKey { get; set; } = default!;
        public ETag ETag { get; set; } = default!;
        public DateTimeOffset? Timestamp { get; set; } = default!;
    }
}