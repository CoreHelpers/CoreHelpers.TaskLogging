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
builder.Services.AddLogging(
    (configure) => configure
        .AddConsole()
        .AddTaskLogger());

builder.Services.AddTransient<IProcessor, ProcessorSuccess>();
builder.Services.AddTransient<IProcessor, ProcessorFailed>();
builder.Services.AddTransient<Worker>();

// build the host
using IHost host = builder.Build();

// get the worker
var worker = host.Services.GetService<Worker>();
if (worker == null)
    throw new NullReferenceException();

// execute the work
await worker.Process();