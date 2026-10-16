using System.Threading.Channels;

namespace Akashic.Host;

public sealed record HostEvent(string Kind, object Data, long AtUnixMs);

/// <summary>
/// Fan-out for the server-sent events stream. Long jobs (download, install, backup)
/// report through <see cref="Progress"/> so the shell can draw a bar without polling.
/// A slow reader drops its oldest events rather than stalling the worker thread.
/// </summary>
public sealed class EventHub
{
    private readonly object _gate = new();
    private readonly List<Channel<HostEvent>> _subscribers = [];

    public Channel<HostEvent> Subscribe()
    {
        var channel = Channel.CreateBounded<HostEvent>(new BoundedChannelOptions(128)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

        lock (_gate) _subscribers.Add(channel);
        return channel;
    }

    public void Unsubscribe(Channel<HostEvent> channel)
    {
        lock (_gate) _subscribers.Remove(channel);
        channel.Writer.TryComplete();
    }

    public void Publish(string kind, object data)
    {
        var hostEvent = new HostEvent(kind, data, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        Channel<HostEvent>[] snapshot;
        lock (_gate) snapshot = [.. _subscribers];
        foreach (var channel in snapshot)
            channel.Writer.TryWrite(hostEvent);
    }

    /// <summary>Progress sink for one job, keyed so several bars can run at once.</summary>
    public IProgress<double> Progress(string kind, string id) =>
        new Progress<double>(fraction => Publish(kind, new { id, fraction }));
}
