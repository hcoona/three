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
using System.Reflection;
using System.Text;

namespace Microsoft.Extensions.Logging.MSTest
{
    /// <summary>
    /// Logger implementation that outputs log messages through the MSTest framework.
    /// </summary>
    /// <remarks>
    /// This logger directs log output to the MSTest Logger, making log messages visible
    /// in test results and Visual Studio Test Output window.
    /// </remarks>
    public class MsTestLogger : ILogger
    {
        private const string LoglevelPadding = ": ";

        private static readonly string MessagePadding = new string(
            ' ',
            GetLogLevelString(LogLevel.Information).Length + LoglevelPadding.Length);

        private static readonly string NewLineWithMessagePadding =
            Environment.NewLine + MessagePadding;

        private static readonly MethodInfo LogMessageMethod = GetLogMessageMethod();

        [ThreadStatic]
#if !NETSTANDARD2_0 && !NET462
        private static StringBuilder? logBuilder;
#else
        private static StringBuilder logBuilder;
#endif

        /// <summary>
        /// Initializes a new instance of the <see cref="MsTestLogger"/> class.
        /// </summary>
        /// <param name="name">The category name for the logger.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="name"/> is null.</exception>
        public MsTestLogger(string name)
        {
            this.Name = name ?? throw new ArgumentNullException(nameof(name));
        }

        /// <summary>
        /// Gets the category name for this logger.
        /// </summary>
        public string Name { get; }

        /// <inheritdoc/>
        public IDisposable BeginScope<TState>(TState state)
        {
            return NullScope.Instance;
        }

        /// <inheritdoc/>
        public bool IsEnabled(LogLevel logLevel) => true;

        /// <inheritdoc/>
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(formatter);
#else
            if (formatter == null)
            {
                throw new ArgumentNullException(nameof(formatter));
            }
#endif

            var message = formatter(state, exception);

            if (!string.IsNullOrEmpty(message) || exception != null)
            {
                this.WriteMessage(logLevel, this.Name, eventId.Id, message, exception);
            }
        }

        /// <summary>
        /// Writes a log message with the specified parameters to the MSTest output.
        /// </summary>
        /// <param name="logLevel">The log level of the message.</param>
        /// <param name="logName">The category name of the logger.</param>
        /// <param name="eventId">The event identifier associated with the log.</param>
        /// <param name="message">The log message.</param>
        /// <param name="exception">The exception associated with the log message, if any.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="logName"/> is null.</exception>
        public virtual void WriteMessage(
            LogLevel logLevel, string logName, int eventId, string message, Exception exception)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(logName);
#else
            if (logName == null)
            {
                throw new ArgumentNullException(nameof(logName));
            }
#endif

            var logBuilder = MsTestLogger.logBuilder;
            MsTestLogger.logBuilder = null;

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
                    Environment.NewLine, NewLineWithMessagePadding, len, message.Length);
            }

            // Format exception details with proper indentation
            if (exception != null)
            {
                logBuilder.Append(MessagePadding);
                var exceptionString = exception.ToString();
                logBuilder.AppendLine(exceptionString.Replace(Environment.NewLine, NewLineWithMessagePadding));
            }

            if (logBuilder.Length > 0)
            {
                var hasLevel = !string.IsNullOrEmpty(logLevelString);

                // Queue log message
                if (hasLevel)
                {
                    WriteLogMessage(logLevelString + logBuilder.ToString().TrimEnd());
                }
                else
                {
                    WriteLogMessage(logBuilder.ToString().TrimEnd());
                }
            }

            logBuilder.Clear();
            if (logBuilder.Capacity > 1024)
            {
                logBuilder.Capacity = 1024;
            }

            MsTestLogger.logBuilder = logBuilder;
        }

        private static void WriteLogMessage(string message)
        {
            LogMessageMethod.Invoke(null, new object[] { message, Array.Empty<object>() });
        }

        private static MethodInfo GetLogMessageMethod()
        {
            var loggerType =
                Type.GetType("Microsoft.VisualStudio.TestTools.UnitTesting.Logging.Logger, MSTest.TestFramework")
                ?? Type.GetType("Microsoft.VisualStudio.TestTools.UnitTesting.Logging.Logger, Microsoft.VisualStudio.TestPlatform.TestFramework");

            if (loggerType == null)
            {
                throw new InvalidOperationException("MSTest logging type could not be loaded.");
            }

            var method = loggerType.GetMethod(
                "LogMessage",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(string), typeof(object[]) },
                null);

            if (method == null)
            {
                throw new InvalidOperationException("MSTest Logger.LogMessage method could not be loaded.");
            }

            return method;
        }

        /// <summary>
        /// Gets the abbreviated string representation of the specified log level.
        /// </summary>
        /// <param name="logLevel">The log level to convert.</param>
        /// <returns>A four-character string representing the log level.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when an unsupported log level is provided.</exception>
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
