using Azure;
using Xunit;

namespace CoreHelpers.TaskLogging.Tests;

public sealed class AzureStorageTableTaskLoggerFactoryTests
{
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
    public async Task ExecuteTableOperation_WhenTableIsMissing_CreatesTableAndRetries()
    {
        var operationCalls = 0;
        var createTableCalls = 0;

        await AzureStorageTableTaskLoggerFactory.ExecuteTableOperation(
            () =>
            {
                operationCalls++;
                return operationCalls == 1
                    ? Task.FromException(new RequestFailedException(404, "Missing table", "TableNotFound", null))
                    : Task.CompletedTask;
            },
            () =>
            {
                createTableCalls++;
                return Task.CompletedTask;
            });

        Assert.Equal(2, operationCalls);
        Assert.Equal(1, createTableCalls);
    }

    [Fact]
    public async Task ExecuteTableOperation_WhenStorageFails_RethrowsException()
    {
        var storageException = new RequestFailedException(503, "Storage unavailable", "ServerBusy", null);

        var thrownException = await Assert.ThrowsAsync<RequestFailedException>(() => AzureStorageTableTaskLoggerFactory.ExecuteTableOperation(
            () => Task.FromException(storageException),
            () => Task.CompletedTask));

        Assert.Same(storageException, thrownException);
    }

    [Fact]
    public async Task ExecuteTableOperation_WhenRetryFails_RethrowsRetryException()
    {
        var operationCalls = 0;
        var retryException = new RequestFailedException(503, "Storage unavailable", "ServerBusy", null);

        var thrownException = await Assert.ThrowsAsync<RequestFailedException>(() => AzureStorageTableTaskLoggerFactory.ExecuteTableOperation(
            () =>
            {
                operationCalls++;
                return operationCalls == 1
                    ? Task.FromException(new RequestFailedException(404, "Missing table", "TableNotFound", null))
                    : Task.FromException(retryException);
            },
            () => Task.CompletedTask));

        Assert.Same(retryException, thrownException);
        Assert.Equal(2, operationCalls);
    }
}