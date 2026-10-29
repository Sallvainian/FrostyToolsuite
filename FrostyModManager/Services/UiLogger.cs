using System;
using Frosty.Sdk.Interfaces;
using FrostyModManager.Models;

namespace FrostyModManager.Services;

/// <summary>
/// Forwards sdk log messages to the ui, the sdk logs from worker threads so the sink takes care of dispatching.
/// </summary>
public sealed class UiLogger : ILogger
{
    private readonly Action<LogLine> m_sink;

    public UiLogger(Action<LogLine> inSink)
    {
        m_sink = inSink;
    }

    public void LogInfo(string message) => m_sink(new LogLine(message, LogLine.InfoBrush));
    public void LogWarning(string message) => m_sink(new LogLine(message, LogLine.WarningBrush));
    public void LogError(string message) => m_sink(new LogLine(message, LogLine.ErrorBrush));

    public void LogProgress(double progress)
    {
        // nothing in the sdk reports progress yet
    }
}
