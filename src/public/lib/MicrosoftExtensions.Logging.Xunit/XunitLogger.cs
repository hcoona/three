// Copyright (c) 2022 Zhang Shuai<zhangshuai.ustc@gmail.com>.
// All rights reserved.
//
// This file is part of OneDotNet.
//
// OneDotNet is free software: you can redistribute it and/or modify it under
// the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version.
//
// OneDotNet is distributed in the hope that it will be useful, but WITHOUT ANY
// WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS
// FOR A PARTICULAR PURPOSE. See the GNU General Public License for more
// details.
//
// You should have received a copy of the GNU General Public License along with
// OneDotNet. If not, see <https://www.gnu.org/licenses/>.

using System;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace Microsoft.Extensions.Logging.Xunit
{
    /// <summary>
    /// Logger implementation that forwards log messages to Xunit's
    /// ITestOutputHelper for test output visibility.
    /// </summary>
    public class XunitLogger : ILogger
    {
        private const string LoglevelPadding = ": ";

        private static readonly string MessagePadding = new string(
            ' ',
            GetLogLevelString(LogLevel.Information).Length + LoglevelPadding.Length);

        private static readonly string NewLineWithMessagePadding =
            Environment.NewLine + MessagePadding;

        [ThreadStatic]
#if !NETSTANDARD2_0 && !NET462
        private static StringBuilder? logBuilder;
#else
        private static StringBuilder logBuilder;
#endif

        private readonly ITestOutputHelper testOutputHelper;

        /// <summary>
        /// Initializes a new instance of the <see cref="XunitLogger"/> class.
        /// </summary>
        /// <param name="testOutputHelper">The test output helper to write log messages to.</param>
        /// <param name="name">The name of the logger category.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when testOutputHelper or name is null.
        /// </exception>
        public XunitLogger(ITestOutputHelper testOutputHelper, string name)
        {
            this.testOutputHelper = testOutputHelper
                ?? throw new ArgumentNullException(nameof(testOutputHelper));
            this.Name = name ?? throw new ArgumentNullException(nameof(name));
        }

        /// <summary>
        /// Gets the name of the logger category.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Begins a logical operation scope for the logger.
        /// </summary>
        /// <typeparam name="TState">The type of state to begin scope with.</typeparam>
        /// <param name="state">The state to begin scope with.</param>
        /// <returns>A disposable scope object. Always returns NullLogger's scope.</returns>
        public IDisposable BeginScope<TState>(TState state)
        {
            return NullLogger.Instance.BeginScope(state);
        }

        /// <summary>
        /// Checks if the given log level is enabled for this logger.
        /// </summary>
        /// <param name="logLevel">The log level to check.</param>
        /// <returns>Always returns true as all log levels are enabled for Xunit output.</returns>
        public bool IsEnabled(LogLevel logLevel) => true;

        /// <summary>
        /// Writes a log entry to the test output.
        /// </summary>
        /// <typeparam name="TState">The type of the state object.</typeparam>
        /// <param name="logLevel">Entry will be written on this level.</param>
        /// <param name="eventId">Id of the event.</param>
        /// <param name="state">The entry to be written. Can be also an object.</param>
        /// <param name="exception">The exception related to this entry.</param>
        /// <param name="formatter">
        /// Function to create a string message of the state and exception.
        /// </param>
        /// <exception cref="ArgumentNullException">Thrown when formatter is null.</exception>
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter)
        {
            if (formatter == null)
            {
                throw new ArgumentNullException(nameof(formatter));
            }

            var message = formatter(state, exception);

            if (!string.IsNullOrEmpty(message) || exception != null)
            {
                this.WriteMessage(logLevel, this.Name, eventId.Id, message, exception);
            }
        }

        /// <summary>
        /// Writes a formatted log message to the test output helper.
        /// </summary>
        /// <param name="logLevel">The log level of the message.</param>
        /// <param name="logName">The name of the logger category.</param>
        /// <param name="eventId">The event identifier for the log entry.</param>
        /// <param name="message">The log message text.</param>
        /// <param name="exception">The exception associated with the log entry, if any.</param>
        /// <exception cref="ArgumentNullException">Thrown when logName is null.</exception>
        public virtual void WriteMessage(
            LogLevel logLevel, string logName, int eventId, string message, Exception exception)
        {
            if (logName == null)
            {
                throw new ArgumentNullException(nameof(logName));
            }

            var logBuilder = XunitLogger.logBuilder;
            XunitLogger.logBuilder = null;

            if (logBuilder == null)
            {
                logBuilder = new StringBuilder();
            }

            // Example:
            // INFO: ConsoleApp.Program[10]
            //       Request received
            var logLevelString = GetLogLevelString(logLevel);

            // category and event id
            logBuilder.Append(LoglevelPadding);
            logBuilder.Append(logName);
            logBuilder.Append('[');
            logBuilder.Append(eventId);
            logBuilder.AppendLine("]");

            if (!string.IsNullOrEmpty(message))
            {
                // message
                logBuilder.Append(MessagePadding);

                var len = logBuilder.Length;
                logBuilder.AppendLine(message);
                logBuilder.Replace(
                    Environment.NewLine,
                    NewLineWithMessagePadding,
                    len,
                    message.Length);
            }

            // Format exception details with proper indentation
            if (exception != null)
            {
                logBuilder.Append(MessagePadding);
                var exceptionString = exception.ToString();
                logBuilder.AppendLine(
                    exceptionString.Replace(
                        Environment.NewLine,
                        NewLineWithMessagePadding));
            }

            if (logBuilder.Length > 0)
            {
                var hasLevel = !string.IsNullOrEmpty(logLevelString);
                var output = logBuilder.ToString().TrimEnd();

                // Write formatted log message to test output
                if (hasLevel)
                {
                    this.testOutputHelper.WriteLine(logLevelString + output);
                }
                else
                {
                    this.testOutputHelper.WriteLine(output);
                }
            }

            logBuilder.Clear();
            if (logBuilder.Capacity > 1024)
            {
                logBuilder.Capacity = 1024;
            }

            XunitLogger.logBuilder = logBuilder;
        }

        private static string GetLogLevelString(LogLevel logLevel)
        {
            switch (logLevel)
            {
                case LogLevel.Trace:
                    return "trce";
                case LogLevel.Debug:
                    return "dbug";
                case LogLevel.Information:
                    return "info";
                case LogLevel.Warning:
                    return "warn";
                case LogLevel.Error:
                    return "fail";
                case LogLevel.Critical:
                    return "crit";
                default:
                    throw new ArgumentOutOfRangeException(nameof(logLevel));
            }
        }
    }
}
