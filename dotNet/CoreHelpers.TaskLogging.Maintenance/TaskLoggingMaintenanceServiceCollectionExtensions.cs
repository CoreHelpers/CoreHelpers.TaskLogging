using Microsoft.Extensions.DependencyInjection;

namespace CoreHelpers.TaskLogging.Maintenance
{
    public static class TaskLoggingMaintenanceServiceCollectionExtensions
    {
        public static IServiceCollection AddTaskLoggingMaintenance(this IServiceCollection services)
        {
            services.AddSingleton<ITaskLoggingMaintenanceService>(_ => new TaskLoggingMaintenanceService());
            return services;
        }
    }
}
