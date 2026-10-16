using System.Text.Json;
using Akashic.Host;

namespace Akashic.Host.Api;

/// <summary>
/// Server-sent events: download / install / backup progress and any state the core
/// changes on its own. Long jobs report through the hub instead of blocking a response.
/// </summary>
public static class EventEndpoints
{
    private static readonly JsonSerializerOptions StreamJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public static RouteGroupBuilder MapEvents(this RouteGroupBuilder group)
    {
        group.MapGet("/events", async (HttpContext http, EventHub hub, CancellationToken ct) =>
        {
            var response = http.Response;
            response.Headers.ContentType = "text/event-stream";
            response.Headers.CacheControl = "no-cache";
            response.Headers["X-Accel-Buffering"] = "no";

            var channel = hub.Subscribe();
            using var aborted = CancellationTokenSource.CreateLinkedTokenSource(ct, http.RequestAborted);

            try
            {
                await response.WriteAsync(": akashic\n\n", aborted.Token);

                await foreach (var hostEvent in channel.Reader.ReadAllAsync(aborted.Token))
                {
                    await response.WriteAsync(
                        $"data: {JsonSerializer.Serialize(hostEvent, StreamJson)}\n\n", aborted.Token);
                    await response.Body.FlushAsync(aborted.Token);
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                hub.Unsubscribe(channel);
            }
        });

        return group;
    }
}
