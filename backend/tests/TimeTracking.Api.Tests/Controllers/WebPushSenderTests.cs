using System.Net;
using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging.Abstractions;
using TimeTracking.Api.Models;
using TimeTracking.Api.Services;

namespace TimeTracking.Api.Tests.Controllers;

/// <summary>The real Web Push sender against a stubbed push service (no network), and the VAPID key helpers.</summary>
public class WebPushSenderTests
{
    private sealed class StubHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public byte[] Content { get; private set; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Request = request;
            Content = request.Content == null ? [] : await request.Content.ReadAsByteArrayAsync(ct);
            return new HttpResponseMessage(status);
        }
    }

    private static PushSubscription Browser()
    {
        using var client = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var q = client.ExportParameters(false).Q;
        return new PushSubscription
        {
            UserId = 1, Endpoint = "https://fcm.googleapis.com/fcm/send/abc",
            P256dh = WebEncoders.Base64UrlEncode([0x04, .. q.X!, .. q.Y!]),
            Auth = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(16))
        };
    }

    private static (WebPushSender Sender, StubHandler Handler) Sender(HttpStatusCode status)
    {
        var (publicKey, privateKey) = VapidKeys.Generate();
        var handler = new StubHandler(status);
        var settings = new WebPushSettings { PublicKey = publicKey, PrivateKey = privateKey, Subject = "mailto:push@example.com" };
        return (new WebPushSender(new HttpClient(handler), settings, NullLogger<WebPushSender>.Instance), handler);
    }

    [Fact]
    public async Task Created_IsSent_EncryptedWithVapid()
    {
        var (sender, handler) = Sender(HttpStatusCode.Created);
        sender.PublicKey.Should().NotBeNullOrEmpty();

        (await sender.SendAsync(Browser(), """{"title":"Hallo"}""", default)).Should().Be(PushSendResult.Sent);

        var request = handler.Request!;
        request.Method.Should().Be(HttpMethod.Post);
        request.RequestUri.Should().Be(new Uri("https://fcm.googleapis.com/fcm/send/abc"));
        request.Headers.Authorization!.Scheme.Should().Be("vapid");
        request.Headers.Authorization.Parameter.Should().MatchRegex("^t=[^,]+, k=" + sender.PublicKey);
        request.Content!.Headers.ContentEncoding.Should().Equal("aes128gcm");
        request.Headers.GetValues("TTL").Single().Should().Be("43200");
        request.Headers.GetValues("Urgency").Single().Should().Be("high");
        handler.Content.Should().NotBeEmpty();
        System.Text.Encoding.UTF8.GetString(handler.Content).Should().NotContain("Hallo");
    }

    [Theory]
    [InlineData(HttpStatusCode.Gone, PushSendResult.Gone)]
    [InlineData(HttpStatusCode.NotFound, PushSendResult.Gone)]
    [InlineData(HttpStatusCode.InternalServerError, PushSendResult.Failed)]
    [InlineData(HttpStatusCode.Forbidden, PushSendResult.Failed)]
    public async Task ErrorStatus_MapsToResult(HttpStatusCode status, PushSendResult expected)
    {
        var (sender, _) = Sender(status);
        (await sender.SendAsync(Browser(), "{}", default)).Should().Be(expected);
    }

    [Fact]
    public async Task Cancellation_IsNotSwallowed()
    {
        var (sender, _) = Sender(HttpStatusCode.Created);
        await sender.Invoking(s => s.SendAsync(Browser(), "{}", new CancellationToken(true)))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task NotConfigured_HasNoKey_AndSendsNothing()
    {
        var handler = new StubHandler(HttpStatusCode.Created);
        var sender = new WebPushSender(new HttpClient(handler), new WebPushSettings { PublicKey = "only-public" }, NullLogger<WebPushSender>.Instance);
        sender.PublicKey.Should().BeNull();
        (await sender.SendAsync(Browser(), "{}", default)).Should().Be(PushSendResult.Failed);
        handler.Request.Should().BeNull();
    }

    [Fact]
    public void Generate_GivesAnUncompressedP256KeyPair()
    {
        var (publicKey, privateKey) = VapidKeys.Generate();
        var q = WebEncoders.Base64UrlDecode(publicKey);
        q.Should().HaveCount(65);
        q[0].Should().Be(0x04);
        WebEncoders.Base64UrlDecode(privateKey).Should().HaveCount(32);
        VapidKeys.Generate().PublicKey.Should().NotBe(publicKey);
    }

    [Fact]
    public void LoadOrCreate_CreatesOnce_ThenReloadsTheSameKeys()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vapid-{Guid.NewGuid():N}.json");
        try
        {
            var created = VapidKeys.LoadOrCreate(path);
            File.Exists(path).Should().BeTrue();
            VapidKeys.LoadOrCreate(path).Should().Be(created);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
