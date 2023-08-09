using System;
using Microsoft.Extensions.Logging;

namespace CoreHelpers.TaskLogging.Sample
{
	internal class ProcessorFailed : IProcessor
    {
        private ILogger<ProcessorFailed> _logger;


        public ProcessorFailed(ILogger<ProcessorFailed> logger)
		{
            _logger = logger;
		}

        public async Task Execute()
        {
            await Task.CompletedTask;

            try
            {
                // log some lines
                for (int i = 0; i < 10; i++)
                    _logger.LogInformation($"{i} Hello World Task 2");

                throw new Exception("I failed!");
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Unknown Error");
            }
        }
    }
}

