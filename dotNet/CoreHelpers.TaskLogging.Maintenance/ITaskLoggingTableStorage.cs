using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure.Data.Tables;

namespace CoreHelpers.TaskLogging.Maintenance
{
    internal interface ITaskLoggingTableStorage
    {
        Task<IReadOnlyList<string>> ListTableNamesAsync(CancellationToken cancellationToken);

        Task<TableEntity?> GetSettingsEntityAsync(
            string tableName,
            string partitionKey,
            string rowKey,
            CancellationToken cancellationToken);

        Task DeleteTableAsync(string tableName, CancellationToken cancellationToken);
    }
}
