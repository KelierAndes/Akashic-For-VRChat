using System.Threading.Channels;

namespace Akashic.Host;

public sealed record HostEvent(string Kind, object Data, long AtUnixMs);

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

    public IProgress<double> Progress(string kind, string id) =>
        new Progress<double>(fraction => Publish(kind, new { id, fraction }));
}
