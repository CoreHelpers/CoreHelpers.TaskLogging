// See https://aka.ms/new-console-template for more information
using CoreHelpers.TaskLogging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using CoreHelpers.Extensions.Logging.Tasks;

// configure the services that they us the emulator
HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

// add the azure storage table logger
builder.Services.AddTaskLoggerForAzureStorageTable("UseDevelopmentStorage=true", "Dev", 100);

// register the task logger framework
builder.Services.AddLogging(
    (configure) => configure
        .AddConsole()
        .AddTaskLogger());

// build the host
using IHost host = builder.Build();

// get the task logger factory
var taskLoggerFactory = host.Services.GetService<ITaskLoggerFactory>();
if (taskLoggerFactory == null)
    throw new NullReferenceException();


// define a bit meta data
var metaData = new Dictionary<string, string>()
{
    { "app", "CoreHelpers.TaskLogging.Sample"},
    { "class", "Main"}
};

// Announce a new task in the system
var taskId = await taskLoggerFactory.AnnounceTask("SyncMyMails", "q:Queue01", "Container01", metaData);

// ... this is the place where the application can do other topics, be aware
// the task is still announced and exists as pending task ...

// get a logger factory
var loggerFactory = host.Services.GetService<ILoggerFactory>();
if (loggerFactory == null)
    throw new NullReferenceException();

// get a logger
var logger = loggerFactory.CreateLogger("Main");

// generate scope
using(logger.BeginScope("This is just a scope"))
{
    using(logger.BeginScope("This is just another scope"))
    {
        // after this using all what is logged becomes part of the
        // task context
        using(logger.BeginTaskScope(taskId))
        {
            // log some lines
            for (int i = 0; i < 500; i++)
                logger.LogInformation($"{i} Hello World Task 1");
        }      
    }

    // announce the second task
    var taskId2 = await taskLoggerFactory.AnnounceTask("SomethingElseWithErrors", "q:Queue01", "Container01");

    // this would be a second task
    using (logger.BeginTaskScope(taskId2))
    {
        try
        {
            // log some lines
            for (int i = 0; i < 10; i++)
                logger.LogInformation($"{i} Hello World Task 2");

            throw new Exception("I failed!");
        } catch (Exception e)
        {
            logger.LogError(e, "Unknown Error");
        }
    }
}

// generate a new task the system never announced before
using(logger.BeginNewTaskScope("NewTask", "Q:Q2", "Cnt02"))
{
    logger.LogInformation("hello new task");
}