// See https://aka.ms/new-console-template for more information
using CoreHelpers.TaskLogging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using CoreHelpers.Extensions.Logging.Tasks;
using CoreHelpers.TaskLogging.Sample;

// configure the services that they us the emulator
HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

// add the azure storage table logger
builder.Services.AddTaskLoggerForAzureStorageTable("UseDevelopmentStorage=true", "Dev", 100);

// register the task logger framework
builder.Services.AddLogging((configure) => configure
    .AddConsole()
    .AddTaskLogger());


builder.Services.AddTransient<IProcessor, ProcessorSuccess>();
builder.Services.AddTransient<IProcessor, ProcessorFailed>();
builder.Services.AddHostedService<Worker>();
builder.Services.AddTransient<WorkerParallelTasks>();

// build the host
using IHost host = builder.Build();

// start the host and execute the registered background worker
await host.RunAsync();