# CoreHelpers.TaskLogging

Task logging helpers for .NET applications that store task state and log messages in Azure Table Storage.

The libraries target `netstandard2.1`, so they can be used from modern .NET applications that support .NET Standard 2.1. The sample project targets `net7.0` and is only intended as an example host.

## Packages

| Package | Target framework | Purpose |
| --- | --- | --- |
| `CoreHelpers.TaskLogging.Abstractions` | `netstandard2.1` | Core task logging interfaces. |
| `CoreHelpers.TaskLogging` | `netstandard2.1` | Azure Table Storage implementation and DI registration. |
| `CoreHelpers.Extensions.Logging.Tasks` | `netstandard2.1` | `Microsoft.Extensions.Logging` integration for task scopes. |
| `CoreHelpers.Extensions.Logging.DurableTask` | `netstandard2.1` | Durable Task / Azure Functions Worker integration. |
| `CoreHelpers.TaskLogging.Maintenance.Abstractions` | `netstandard2.1` | Maintenance options, result types, constants, and service interface. |
| `CoreHelpers.TaskLogging.Maintenance` | `netstandard2.1` | Azure Table Storage cleanup implementation and DI registration. |

## Logger Setup

Install the packages required by your application. A typical hosted app that writes task logs through `ILogger` uses:

```bash
dotnet add package CoreHelpers.TaskLogging
dotnet add package CoreHelpers.Extensions.Logging.Tasks
```

Register the Azure Table Storage task logger:

```csharp
builder.Services.AddTaskLoggerForAzureStorageTable(
    connectionString: "UseDevelopmentStorage=true",
    environmentPrefix: "Dev",
    lineCacheLimit: 100);
```

`environmentPrefix` becomes the table prefix. Monthly tables are created with names like `Dev202606Tasks` and `Dev202606Messages`.

Use task scopes through `ILogger`:

```csharp
using (_logger.BeginNewTaskScope("ImportJob", "Queue", "Worker-1"))
{
    _logger.LogInformation("Processing started");
}
```

## Maintenance Cleanup

`CoreHelpers.TaskLogging.Maintenance` can delete old, month-based task logging tables that were created by `CoreHelpers.TaskLogging`.

Install and register the maintenance package:

```bash
dotnet add package CoreHelpers.TaskLogging.Maintenance
```

```csharp
builder.Services.AddTaskLoggingMaintenance();
```

This registers `ITaskLoggingMaintenanceService` as a singleton. The concrete implementation is internal; consumers should depend on the interface.

Run cleanup through the service:

```csharp
var result = await maintenanceService.CleanupAsync(new TaskLoggingCleanupOptions
{
    ConnectionString = "UseDevelopmentStorage=true",
    TaskLoggerPrefix = "Dev",
    DryRun = true
});
```

`DryRun` defaults to `true`. In dry-run mode, deletion candidates are returned in `DeletedTables`, but no Azure tables are deleted.

### Retention Configuration

Cleanup is intentionally disabled unless retention is configured in Azure Table Storage. The service reads the retention value from this entity:

- Table: `{TaskLoggerPrefix}Settings`
- PartitionKey: `Maintenance`
- RowKey: `LogCleanup`
- Property: `LogRetentionMonths`

`LogRetentionMonths` must be a positive integer. If the settings table, entity, property, or a valid positive value is missing, the service returns `RotationConfigured = false` and does not delete any tables.

Example for prefix `Dev`:

| Table | PartitionKey | RowKey | LogRetentionMonths |
| --- | --- | --- | --- |
| `DevSettings` | `Maintenance` | `LogCleanup` | `6` |

With `LogRetentionMonths = 6`, the cleanup keeps the current month and the configured retention window. Tables older than the calculated cutoff month are deletion candidates.

The cleanup service only considers rotatable monthly tables with these suffixes:

- `Tasks`
- `Messages`
- `TasksFailed`
- `TasksExternalIdLookup`

The non-rotated running table (`{TaskLoggerPrefix}TasksRunning`) and the settings table are not deleted by the maintenance cleanup.

## Build

```bash
cd dotNet
dotnet build TaskLogging.sln
dotnet test TaskLogging.sln
```

Release builds publish the NuGet packages through the GitHub Actions release workflow.
