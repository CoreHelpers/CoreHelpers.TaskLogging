using System;
using Microsoft.Extensions.Logging;
using CoreHelpers.Extensions.Logging.Tasks;
using Microsoft.Extensions.Hosting;

namespace CoreHelpers.TaskLogging.Sample
{
	internal class Worker
	{
		private readonly ILogger<Worker> _logger;
		private readonly IEnumerable<IProcessor> _processors;
        private readonly ITaskLoggerFactory _taskLoggerFactory;
        private readonly IHostApplicationLifetime _appLifetime;
        
        public Worker(ILogger<Worker> logger, IEnumerable<IProcessor> processors, ITaskLoggerFactory taskLoggerFactory, IHostApplicationLifetime appLifetime)
		{
			_logger = logger;
			_processors = processors;
            _taskLoggerFactory = taskLoggerFactory;
            _appLifetime = appLifetime;
		}

		public async Task Process()
		{           
            // execute the success processor
            using (_logger.BeginNewTaskScope("successjob", "q", "w"))
			{				
				await _processors.Where(p => p is ProcessorSuccess).First().Execute();								
			}

            // execute the failedprocessor
            using (var scope = _logger.BeginNewTaskScope("failedjob", "q", "w", "app=CoreHelpers.TaskLogging.Sample,class=Main"))
            {
	            Console.WriteLine(scope.TaskId);
                await _processors.Where(p => p is ProcessorFailed).First().Execute();
            }

            // execute the succssprocesssor with announcement            
            var metaData = new Dictionary<string, string>()
            {
                { "app", "CoreHelpers.TaskLogging.Sample"},
                { "class", "Main"}
            };

            var taskId = await _taskLoggerFactory.AnnounceTask("announcejob", "q", "w", metaData);

            using (_logger.BeginTaskScope(taskId))
            {
                await _processors.Where(p => p is ProcessorSuccess).First().Execute();
            }
            
            // execute the success processor
            using (var typedLoggerTaskScope = _logger.BeginNewTaskScope("successjob", "q", "w"))
            {
	            // in the scope of the task a shutdown handler will be registered
	            _appLifetime.ApplicationStopping.Register(() =>
	            {
		            _logger.LogError("We are shutting down the application");
					if (typedLoggerTaskScope != null)
						typedLoggerTaskScope.Dispose();
					
	            });
	            
	            // log something
	            _logger.LogInformation("Will be logged in the timespan");
	            await Task.Delay(TimeSpan.FromSeconds(60));
	            
	            // log something
	            _logger.LogInformation(("Logging something we should see after graceful shurtdown"));
	            
	            // trigger a graceful shutdown
	            _appLifetime.StopApplication();
	            
	            // prevent the application code to leave the scope
	            await Task.Delay(TimeSpan.FromHours(1));
	            
	            // log something we never see again
	            _logger.LogInformation(("This should never be shown"));
            }
            
        }
    }
}

