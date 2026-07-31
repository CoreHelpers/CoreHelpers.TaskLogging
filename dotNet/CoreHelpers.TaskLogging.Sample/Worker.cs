using System;
using Microsoft.Extensions.Logging;
using CoreHelpers.Extensions.Logging.Tasks;
using Microsoft.Extensions.Hosting;

namespace CoreHelpers.TaskLogging.Sample
{
	internal class Worker : BackgroundService
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

		protected override async Task ExecuteAsync(CancellationToken stoppingToken)
		{           
            // execute the success processor
            using (_logger.BeginNewTaskScope("successjob", "q", "w"))
			{				
				await _processors.Where(p => p is ProcessorSuccess).First().Execute(stoppingToken);
			}

            // execute the failedprocessor
            using (var scope = _logger.BeginNewTaskScope("failedjob", "q", "w", "app=CoreHelpers.TaskLogging.Sample,class=Main"))
	            {
		            Console.WriteLine(scope.TaskId);
	                await _processors.Where(p => p is ProcessorFailed).First().Execute(stoppingToken);
	            }

            // execute the succssprocesssor with announcement            
            var metaData = new Dictionary<string, string>()
            {
                { "app", "CoreHelpers.TaskLogging.Sample"},
                { "class", "Main"}
            };

            var taskId = await _taskLoggerFactory.AnnounceTask("announcejob", "q", "w", metaData).WaitAsync(stoppingToken);
			var externalId = Guid.NewGuid().ToString();

			await _taskLoggerFactory.RegisterExternlIdForTask(taskId, externalId).WaitAsync(stoppingToken);
            using (_logger.BeginTaskScope(taskId))
            {
	            var lookedUpTaskId = await _taskLoggerFactory.LookupTaskIdByExternalId(externalId).WaitAsync(stoppingToken);
	            if (lookedUpTaskId != taskId)
		            throw new Exception($"Task with id {lookedUpTaskId} was not found");
	            
                await _processors.Where(p => p is ProcessorSuccess).First().Execute(stoppingToken);
            }
            
            // execute the success processor
            using (var typedLoggerTaskScope = _logger.BeginNewTaskScope("successjob", "q", "w"))
            {
	            // log something
	            _logger.LogInformation("Will be logged in the timespan");
	            await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
	            
	            // log something
	            _logger.LogInformation(("Logging something we should see after graceful shurtdown"));
	            
	            // trigger a graceful shutdown
	            _appLifetime.StopApplication();
	            
	            // prevent the application code to leave the scope
	            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
	            
	            // log something we never see again
	            _logger.LogInformation(("This should never be shown"));
            }
            
        }
    }
}