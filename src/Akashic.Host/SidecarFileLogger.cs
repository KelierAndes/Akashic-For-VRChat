using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Akashic.Host;

public sealed class SidecarFileLoggerProvider : ILoggerProvider, IDisposable
{
    private const long MaxBytes = 2L * 1024 * 1024;

    private readonly BlockingCollection<string> _queue = new(4096);
    private readonly string _path;
    private readonly CancellationTokenSource _closing = new();
    private readonly Task _pump;

    public SidecarFileLoggerProvider(string path)
    {
        _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _pump = Task.Run(Pump);
    }

    public ILogger CreateLogger(string categoryName) => new SidecarFileLogger(this, categoryName);

    public void Dispose()
    {
        _closing.Cancel();
        _queue.CompleteAdding();
        try
        {
            _pump.Wait(TimeSpan.FromSeconds(2));
        }
        catch (Exception)
        {
        }

        _queue.Dispose();
        _closing.Dispose();
    }

    internal void Enqueue(string line)
    {
        try
        {
            _queue.Add(line);
        }
        catch (Exception)
        {
        }
    }

    private void Pump()
    {
        var token = _closing.Token;
        try
        {
            foreach (var line in _queue.GetConsumingEnumerable(token))
                Append(line);

            while (!_queue.IsCompleted)
                if (_queue.TryTake(out var line, Timeout.Infinite, token))
                    Append(line);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void Append(string line)
    {
        try
        {
            var info = new FileInfo(_path);
            if (info.Exists && info.Length > MaxBytes)
            {
                var backup = _path + ".1";
                if (File.Exists(backup)) File.Delete(backup);
                File.Move(_path, backup);
            }

            File.AppendAllText(_path, line + Environment.NewLine);
        }
        catch (Exception)
        {
        }
    }
}

public sealed class SidecarFileLogger : ILogger
{
    private static readonly LogLevel Threshold = LogLevel.Information;

    private readonly SidecarFileLoggerProvider _provider;
    private readonly string _category;

    public SidecarFileLogger(SidecarFileLoggerProvider provider, string category)
    {
        _provider = provider;
        _category = category;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= Threshold;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var text = formatter(state, exception);
        if (string.IsNullOrWhiteSpace(text) && exception is null) return;

        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {Level(logLevel)} {_category} {text}";
        if (exception is not null)
            line += Environment.NewLine + "    " + exception.ToString().Replace(Environment.NewLine, Environment.NewLine + "    ");

        _provider.Enqueue(line);
    }

    private static string Level(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "FTL",
        _ => "???"
    };
}
