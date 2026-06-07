using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;

namespace CoreHelpers.TaskLogging.Maintenance
{
    internal class AzureTaskLoggingTableStorage : ITaskLoggingTableStorage
    {
        private readonly TableServiceClient _tableServiceClient;

        public AzureTaskLoggingTableStorage(string connectionString)
        {
            _tableServiceClient = new TableServiceClient(connectionString);
        }

        public async Task<IReadOnlyList<string>> ListTableNamesAsync(CancellationToken cancellationToken)
        {
            var tableNames = new List<string>();

            await foreach (var table in _tableServiceClient.QueryAsync(cancellationToken: cancellationToken))
                tableNames.Add(table.Name);

            return tableNames;
        }

        public async Task<TableEntity?> GetSettingsEntityAsync(
            string tableName,
            string partitionKey,
            string rowKey,
            CancellationToken cancellationToken)
        {
            try
            {
                var tableClient = _tableServiceClient.GetTableClient(tableName);
                return await tableClient.GetEntityAsync<TableEntity>(partitionKey, rowKey, cancellationToken: cancellationToken);
            }
            catch (RequestFailedException ex) when (ex.Status == 404 || ex.ErrorCode == "TableNotFound" || ex.ErrorCode == "ResourceNotFound")
            {
                return null;
            }
        }

        public async Task DeleteTableAsync(string tableName, CancellationToken cancellationToken)
        {
            try
            {
                await _tableServiceClient.DeleteTableAsync(tableName, cancellationToken);
            }
            catch (RequestFailedException ex) when (ex.Status == 404 || ex.ErrorCode == "TableNotFound" || ex.ErrorCode == "ResourceNotFound")
            {
            }
        }
    }
}
