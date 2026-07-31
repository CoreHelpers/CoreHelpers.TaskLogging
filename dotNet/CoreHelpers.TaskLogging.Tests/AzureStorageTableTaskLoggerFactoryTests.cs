using Azure;
using Xunit;

namespace CoreHelpers.TaskLogging.Tests;

public sealed class AzureStorageTableTaskLoggerFactoryTests
{
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