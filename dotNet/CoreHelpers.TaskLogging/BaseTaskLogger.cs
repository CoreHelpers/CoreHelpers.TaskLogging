using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace CoreHelpers.TaskLogging
{
	internal abstract class BaseTaskLogger : ITaskLogger
    {
        private int _cacheLimit;
        private readonly TimeSpan _cacheTimeSpan;
        private string _taskId;
        private List<string> _messages = new List<string>();
        private readonly Timer _flushTimer;
        
        public BaseTaskLogger(string taskId, int cacheLimit, TimeSpan cacheTimeSpan)
        {
            _taskId = taskId;
            _cacheLimit = cacheLimit;
            _cacheTimeSpan = cacheTimeSpan;
            _flushTimer = new Timer((state) => {
                FlushInternal();
            }, null, _cacheTimeSpan, _cacheTimeSpan);
        }

        public void Log(TaskLogLevel logLevel, string message, Exception? exception)
        {
            Log(logLevel, message, exception, (string message, Exception? e) =>
            {
                var prefix = $"[{logLevel.ToString()}] [{DateTime.UtcNow.ToString("MM/dd/yyyy HH:mm:ss")}]";
                return $"{prefix} - {message}";                
            });
        }

        public void Log(TaskLogLevel logLevel, string message, Exception? exception, Func<string, Exception?, string> formatter)
        {
            if (exception != null)
                LogException(logLevel, exception, formatter);
            else
            {
                // Add the message to the queue
                lock (_messages)
                {
                    _messages.Add(formatter(message, exception));
                }

                
                // flush if needed
                if (_messages.Count >= _cacheLimit)
                    FlushInternal();
            }
        }

        public void Dispose()
        {
            FlushInternal();
            _flushTimer.Dispose();
        }
        
        private void FlushInternal()
        {
            lock(_messages)
            {
                Flush(_taskId, _messages).GetAwaiter().GetResult();
                _messages = new List<string>();
            }
        }

        public abstract Task Flush(string taskId,IEnumerable<string> messages);

        private void LogException(TaskLogLevel logLevel, Exception exception, Func<string, Exception?, string> formatter, bool innerException = false)
        {
            if (innerException)
                Log(logLevel, $"Inner Exception: {exception.Message}", null, formatter);
            else
            {
                Log(logLevel, $"Error with exception: {exception.Message}", null, formatter);                
                try
                {
                    Log(logLevel, "Dumping Raw-JSON-Export of the exception", null, formatter);
                    Log(logLevel, JsonConvert.SerializeObject(exception), null, formatter);
                }
                catch (Exception)
                {
                    // The exception is catched without handling to prevent
                    // crashed just because of invalid JSON message we try to log
                }
            }

            if (exception.StackTrace != null)
            {
                var splittedError = exception.StackTrace.Split('\n');
                foreach (var el in splittedError)
                    Log(logLevel, el, null, formatter);
            }

            if (exception.InnerException != null)
                LogException(logLevel, exception.InnerException, formatter, true);
        }
    }
}

