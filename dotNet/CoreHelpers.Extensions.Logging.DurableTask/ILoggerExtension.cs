using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;

namespace CoreHelpers.Extensions.Logging.DurableTask
{
    public static class LoggerExtension
    {
        public static void LogInformationReplayingAware(this ILogger logger, TaskOrchestrationContext context, string? message, params object?[] args)
        {
            if (!context.IsReplaying)
                logger.LogInformation(message, args);
        }
    }

}