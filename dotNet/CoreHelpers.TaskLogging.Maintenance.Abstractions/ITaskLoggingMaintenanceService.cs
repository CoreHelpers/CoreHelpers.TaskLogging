using System.Threading;
using System.Threading.Tasks;

namespace CoreHelpers.TaskLogging.Maintenance
{
    public interface ITaskLoggingMaintenanceService
    {
        Task<TaskLoggingCleanupResult> CleanupAsync(
            TaskLoggingCleanupOptions options,
            CancellationToken cancellationToken = default);
    }
}
