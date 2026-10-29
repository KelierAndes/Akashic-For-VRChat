using System.Collections.Concurrent;

namespace Akashic.Host.Preview.Hlsl;

public sealed class KeyedSemaphore : IDisposable
{
    private readonly SemaphoreSlim _global;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _keys = new(StringComparer.OrdinalIgnoreCase);

    public KeyedSemaphore(int maxConcurrent)
    {
        var limit = Math.Max(1, maxConcurrent);
        _global = new SemaphoreSlim(limit, limit);
    }

    public static int DefaultLimit => Math.Clamp(Environment.ProcessorCount / 2, 1, 4);

    public static string KeyOf(string path)
    {
        try
        {
            return Path.GetFullPath(path).ToLowerInvariant();
        }
        catch (Exception)
        {
            return path.ToLowerInvariant();
        }
    }

    public async Task<IDisposable> AcquireAsync(string key, CancellationToken ct)
    {
        await _global.WaitAsync(ct).ConfigureAwait(false);

        var gate = _keys.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        try
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            _global.Release();
            throw;
        }

        return new Lease(_global, gate);
    }

    public void Dispose() => _global.Dispose();

    private sealed class Lease(SemaphoreSlim global, SemaphoreSlim key) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 1) return;
            key.Release();
            global.Release();
        }
    }
}
