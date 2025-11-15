using CoreHelpers.Extensions.Logging.Tasks;
using CoreHelpers.TaskLogging;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;

namespace CoreHelpers.Extensions.Logging.DurableTask
{
    public static class DurableTaskClientExtension
    {
        public static ITaskLoggerScope? BeginOrchestratorTaskLoggerContext(this ILogger logger, TaskOrchestrationContext context, ITaskLoggerFactory taskLoggerFactory)
        {
            // lookup the task log id 
            var tasklogId = taskLoggerFactory.LookupTaskIdByExternalId(context.InstanceId).GetAwaiter().GetResult();
            if (string.IsNullOrEmpty(tasklogId))
            {
                tasklogId = taskLoggerFactory.AnnounceTask(context.Name, context.Parent == null ? "OrchestratorInvocation" : "SubOrchestratorInvocation", "AzureFunctionsFlexConsumption").GetAwaiter().GetResult();
                taskLoggerFactory.RegisterExternlIdForTask(tasklogId, context.InstanceId).GetAwaiter().GetResult();
            }

            // return the context
            return logger.BeginTaskScope(tasklogId);
        }
    }
}