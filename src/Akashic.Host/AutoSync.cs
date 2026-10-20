using Akashic.Core.Services;

namespace Akashic.Host;

public sealed class AutoSync
{
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(10);

    private readonly IConfigSyncService _sync;
    private readonly object _gate = new();

    private int _generation;
    private bool _running;
    private bool _again;
    private string _reason = string.Empty;
    private string _lastReason = string.Empty;
    private DateTime? _lastPushedAt;

    public AutoSync(IConfigSyncService sync)
    {
        _sync = sync;
    }

    public void Queue(string reason)
    {
        int generation;

        lock (_gate)
        {
            _reason = reason;
            generation = ++_generation;
        }

        _ = Task.Run(() => PushLaterAsync(generation));
    }

    public object Snapshot()
    {
        lock (_gate)
        {
            return new
            {
                lastReason = _lastReason,
                lastPushedAt = _lastPushedAt?.ToString("yyyy-MM-dd HH:mm:ss"),
                running = _running
            };
        }
    }

    private async Task PushLaterAsync(int generation)
    {
        await Task.Delay(Debounce);

        lock (_gate)
        {
            if (generation != _generation) return;

            if (_running)
            {
                _again = true;
                return;
            }

            _running = true;
            _lastReason = _reason;
        }

        try
        {
            var result = await _sync.PushAsync(CancellationToken.None);
            if (result.Success)
            {
                lock (_gate) _lastPushedAt = DateTime.Now;
            }
        }
        catch (Exception)
        {
        }
        finally
        {
            bool again;

            lock (_gate)
            {
                _running = false;
                again = _again;
                _again = false;
            }

            if (again) Queue(_reason);
        }
    }
}
