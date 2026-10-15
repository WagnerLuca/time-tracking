using System.Collections.Concurrent;
using System.Text.Json;
using TimeTracking.Api.Models;
using TimeTracking.Api.Services;

namespace TimeTracking.Api.Tests.Infrastructure;

/// <summary>
/// Records pushes instead of sending them. <see cref="PushDispatcher"/> delivers in the
/// background, so tests wait with <see cref="WaitForAsync"/>.
/// </summary>
public class FakePushSender : IPushSender
{
    public record Push(string Endpoint, string Payload)
    {
        private JsonElement Json => JsonDocument.Parse(Payload).RootElement;
        public string Title => Json.GetProperty("title").GetString()!;
        public string Body => Json.GetProperty("body").GetString()!;
        public string Url => Json.GetProperty("url").GetString()!;
        public string Tag => Json.GetProperty("tag").GetString()!;
    }

    public string? PublicKey { get; set; } = "BFakeVapidPublicKeyForTests";

    public ConcurrentQueue<Push> Sent { get; } = new();

    /// <summary>Endpoints the fake push service does not accept: Gone (410) or Failed (5xx); nothing is recorded for them.</summary>
    public ConcurrentDictionary<string, PushSendResult> Answers { get; } = new();

    public Task<PushSendResult> SendAsync(PushSubscription subscription, string payload, CancellationToken ct)
    {
        if (Answers.TryGetValue(subscription.Endpoint, out var answer)) return Task.FromResult(answer);
        Sent.Enqueue(new Push(subscription.Endpoint, payload));
        return Task.FromResult(PushSendResult.Sent);
    }

    public List<Push> To(string endpoint) => Sent.Where(p => p.Endpoint == endpoint).ToList();

    /// <summary>The pushes to <paramref name="endpoint"/> matching <paramref name="match"/>, once there are any (≤ 5 s).</summary>
    public async Task<List<Push>> WaitForAsync(string endpoint, Func<Push, bool>? match = null)
    {
        for (var deadline = DateTime.UtcNow.AddSeconds(5); ; await Task.Delay(20))
        {
            var found = To(endpoint).Where(p => match == null || match(p)).ToList();
            if (found.Count > 0 || DateTime.UtcNow > deadline) return found;
        }
    }
}
