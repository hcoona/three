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
using System.IO;
using System.Threading;

namespace Hcoona.Nanopass;

/// <summary>
/// Captures Nanopass trace events in the current asynchronous execution context.
/// </summary>
public static class PassTrace
{
    private static readonly AsyncLocal<Action<PassTraceEvent>?> CurrentSink = new();

    /// <summary>
    /// Gets a value indicating whether the current context is capturing trace events.
    /// </summary>
    public static bool IsEnabled => CurrentSink.Value is not null;

    /// <summary>
    /// Installs a trace sink for the current asynchronous execution context.
    /// </summary>
    /// <param name="sink">The callback to receive trace events.</param>
    /// <returns>A scope that restores the previous sink when disposed.</returns>
    /// <remarks>Dispose nested scopes in reverse order in the context that created them.</remarks>
    public static IDisposable Capture(Action<PassTraceEvent> sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        return new CaptureScope(sink);
    }

    /// <summary>
    /// Captures and writes a diagnostic line for each trace event.
    /// </summary>
    /// <param name="writer">The writer receiving the lines. The caller owns it.</param>
    /// <returns>A scope that restores the previous sink when disposed.</returns>
    /// <remarks>Writes values without invoking pass unparsers.</remarks>
    public static IDisposable Capture(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        TextWriter synchronized = TextWriter.Synchronized(writer);
        return Capture(traceEvent =>
        {
            string name = traceEvent.ProcessorName is null
                ? traceEvent.PassName
                : $"{traceEvent.PassName}.{traceEvent.ProcessorName}";
            string value = traceEvent.Value?.ToString() ?? "null";
            synchronized.WriteLine($"{name} {traceEvent.Phase}: {value}");
        });
    }

    /// <summary>
    /// Sends an event to the current trace sink, if one is installed.
    /// </summary>
    /// <param name="traceEvent">The event to report.</param>
    public static void Emit(PassTraceEvent traceEvent)
    {
        ArgumentNullException.ThrowIfNull(traceEvent);
        CurrentSink.Value?.Invoke(traceEvent);
    }

    private sealed class CaptureScope : IDisposable
    {
        private readonly Action<PassTraceEvent>? previousSink;
        private readonly Action<PassTraceEvent> installedSink;

        internal CaptureScope(Action<PassTraceEvent> sink)
        {
            this.previousSink = CurrentSink.Value;
            this.installedSink = sink.Invoke;
            CurrentSink.Value = this.installedSink;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (ReferenceEquals(CurrentSink.Value, this.installedSink))
            {
                CurrentSink.Value = this.previousSink;
            }
        }
    }
}
