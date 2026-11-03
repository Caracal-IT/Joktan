using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Joktan.Core;

/// <summary>
/// Writes logs to a file for each day, such as joktan-2026-11-02.log, from a thread of its own so that
/// logging never waits for the disk. Files past the number of days kept are deleted.
/// </summary>
public sealed class FileLogSink : IDisposable
{
    private const int MaxQueued = 20000;
    private const string Prefix = AppLog.AppName + "-";

    private readonly Func<(string Folder, int KeepDays)> _where;
    private readonly Func<DateTime> _today;
    private readonly List<string> _queue = [];
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;
    private readonly TimeSpan _interval;
    private DateTime _lastTidied;

    /// <param name="where">Asked before each write, so that a change on the settings page applies at once.</param>
    public FileLogSink(Func<(string Folder, int KeepDays)> where, TimeSpan? interval = null, Func<DateTime>? today = null)
    {
        _where = where;
        _today = today ?? (() => DateTime.Now.Date);
        _interval = interval ?? TimeSpan.FromSeconds(1);
        _loop = Task.Run(RunAsync);
    }

    /// <summary>Why the last write failed, or null if it worked.</summary>
    public static string? LastProblem { get; private set; }

    public void Add(string line)
    {
        lock (_queue)
        {
            if (_queue.Count >= MaxQueued)
                _queue.RemoveAt(0);
            _queue.Add(line);
        }
    }

    private async Task RunAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_interval, _stop.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            Flush();
        }
    }

    /// <summary>Writes what is waiting. What could not be written stays for the next time.</summary>
    public void Flush()
    {
        string[] lines;
        lock (_queue)
            lines = _queue.ToArray();
        if (lines.Length == 0)
            return;

        var (folder, keepDays) = _where();
        try
        {
            Directory.CreateDirectory(folder);
            var today = _today();
            File.AppendAllLines(Path.Combine(folder, $"{Prefix}{today:yyyy-MM-dd}.log"), lines, new UTF8Encoding(false));
            lock (_queue)
                _queue.RemoveRange(0, lines.Length);
            LastProblem = null;

            if (_lastTidied != today)
            {
                _lastTidied = today;
                Tidy(folder, today, keepDays);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            LastProblem = e.Message;
        }
    }

    // Only files with Joktan's own name and a date are ever deleted.
    private static void Tidy(string folder, DateTime today, int keepDays)
    {
        foreach (var file in Directory.EnumerateFiles(folder, Prefix + "*.log"))
        {
            var name = Path.GetFileNameWithoutExtension(file)[Prefix.Length..];
            if (DateTime.TryParseExact(name, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var date) && (today - date).TotalDays >= keepDays)
                File.Delete(file);
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        try
        {
            _loop.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // Closing goes on regardless.
        }

        Flush();
        _stop.Dispose();
    }
}

/// <summary>Turns logs into lines naming the time, level, app and module.</summary>
public sealed class FileLoggerProvider(FileLogSink sink) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new Logger(sink, categoryName);

    public void Dispose()
    {
    }

    private sealed class Logger(FileLogSink sink, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => AppLog.Settings.LogsToFile && logLevel >= AppLog.Settings.MinimumLevel && logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            var line = $"{DateTimeOffset.Now:yyyy-MM-ddTHH:mm:ss.fffzzz} {logLevel.ToString().ToUpperInvariant()} app={AppLog.AppName} module={AppLog.ModuleOf(category)} {formatter(state, exception)}";
            sink.Add(exception is null ? line : line + Environment.NewLine + exception);
        }
    }
}
