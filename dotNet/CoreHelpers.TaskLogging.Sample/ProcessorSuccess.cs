using System;
using Microsoft.Extensions.Logging;

namespace CoreHelpers.TaskLogging.Sample
{
	internal class ProcessorSuccess : IProcessor
    {
        private ILogger<ProcessorSuccess> _logger;


        public ProcessorSuccess(ILogger<ProcessorSuccess> logger)
		{
            _logger = logger;
		}

        public async Task Execute()
        {
            _logger.LogInformation("Hello from the processor");
            await Task.CompletedTask;
        }
    }
}

