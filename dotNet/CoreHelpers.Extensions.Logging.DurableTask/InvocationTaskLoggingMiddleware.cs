using System;
using System.Linq;
using System.Threading.Tasks;
using CoreHelpers.Extensions.Logging.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CoreHelpers.Extensions.Logging.DurableTask
{
    public static class FunctionsApplicationBuilderExtensions
    {
        public static FunctionsApplicationBuilder UseTaskLoggingMiddleware(this FunctionsApplicationBuilder builder)
        {
            builder.UseMiddleware<InvocationTaskLoggingMiddleware>();
            return builder;
        }
    }

    internal class InvocationTaskLoggingMiddleware : IFunctionsWorkerMiddleware
    {
        public Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
        {
            // figure out if it is an orchestration function
            bool isOrchestration = context.FunctionDefinition.InputBindings.Values
                .Any(b => b.Type.EndsWith("orchestrationTrigger", StringComparison.OrdinalIgnoreCase));

            if (isOrchestration)
                return next(context);
            
            var logger = context.GetLogger(context.FunctionDefinition.Name);

            using (logger.BeginNewTaskScope(context.FunctionDefinition.Name, "ActivityInvocation", "AzureFunctionsFlexConsumption"))
            {
                try
                {
                    return next(context);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "An unhandled exception occurred during function invocation.");
                    throw;
                }
            }
        }
    }
}