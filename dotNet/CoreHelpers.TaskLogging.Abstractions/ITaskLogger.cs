using System;
namespace CoreHelpers.TaskLogging
{
    public enum TaskLogLevel {
        Information,
        Error
    }

	public interface ITaskLogger : IDisposable
	{
        void Log(TaskLogLevel logLevel, string message, Exception? exception, Func<string, Exception?, string> formatter);

        void Log(TaskLogLevel logLevel, string message, Exception? exception);
    }
}

