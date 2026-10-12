using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Yingyeothon.Codec;
using Yingyeothon.Platform;

namespace Yingyeothon.Push
{
    /// <summary>Push wire configuration; Firebase remains owned by the game.</summary>
    public sealed class PushClientOptions
    {
        /// <summary>HTTP options carrying the player's JWT, never a server key.</summary>
        public PlatformClientOptions Http { get; set; } = new PlatformClientOptions();
        /// <summary>Push channel id from kit config.</summary>
        public string ChannelId { get; set; } = "";
    }
    /// <summary>FCM registration supplied by the app.</summary>
    public sealed class PushRegisterOptions
    {
        /// <summary>Device FCM registration token. Never log or persist it outside device storage.</summary>
        public string Token { get; set; } = "";
        /// <summary>Firebase project id from the game's embedded Firebase configuration.</summary>
        public string Project { get; set; } = "";
    }
    /// <summary>Classification of an incoming message, never trusted as a ticket answer.</summary>
    public enum PushMessageKind { Message, Match }
    /// <summary>Push registration core, pumped by its owner.</summary>
    public interface IPushClient : IPollable
    {
        /// <summary>Registers on every app start and FCM token refresh.</summary>
        Task RegisterAsync(PushRegisterOptions options, CancellationToken cancellationToken = default);
        /// <summary>Removes the named device token on sign-out.</summary>
        Task UnregisterAsync(string token, CancellationToken cancellationToken = default);
    }
    /// <summary>Core factory and pure message classifier.</summary>
    public static class PushClient
    {
        /// <summary>Creates a core without a Firebase SDK dependency.</summary>
        public static IPushClient Create(PushClientOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            return new PushClientImpl(PlatformHttpClient.Create(options.Http), "/push/" + PlatformHttpClient.Segment(options.ChannelId) + "/token");
        }
        /// <summary>Recognizes match wakeups; the caller must still fetch authoritative status.</summary>
        public static PushMessageKind Classify(IReadOnlyDictionary<string, string> data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            return data.TryGetValue("channelId", out var channel) && !string.IsNullOrEmpty(channel)
                && data.ContainsKey("matchId") && data.TryGetValue("state", out var state)
                && (state == "waiting" || state == "proposed" || state == "confirmed" || state == "expired" || state == "declined" || state == "failed")
                ? PushMessageKind.Match : PushMessageKind.Message;
        }
    }
    internal sealed class PushClientImpl : IPushClient
    {
        private readonly IPlatformHttpClient _http; private readonly string _path;
        internal PushClientImpl(IPlatformHttpClient http, string path) { _http = http; _path = path; }
        private static void Token(string token)
        {
            if (string.IsNullOrEmpty(token) || token.Length > 512)
            {
                throw new ArgumentException("Invalid registration token", nameof(token));
            }

            foreach (var c in token)
            {
                if (c < 33 || c > 126)
                {
                    throw new ArgumentException("Invalid registration token", nameof(token));
                }
            }
        }
        public Task RegisterAsync(PushRegisterOptions options, CancellationToken ct = default)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            Token(options.Token);
            if (string.IsNullOrEmpty(options.Project) || options.Project.Length > 64 || !char.IsLetterOrDigit(options.Project[0]))
            {
                throw new ArgumentException("Invalid Firebase project", nameof(options));
            }

            foreach (var c in options.Project)
            {
                if (!(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && c != '-')
                {
                    throw new ArgumentException("Invalid Firebase project", nameof(options));
                }
            }

            return _http.RequestAsync(new PlatformRequestOptions { Path = _path, Method = "PUT", Body = Json.Object().Set("token", options.Token).Set("project", options.Project).Build() }, ct);
        }
        public Task UnregisterAsync(string token, CancellationToken ct = default)
        {
            Token(token);
            return _http.RequestAsync(new PlatformRequestOptions { Path = _path, Method = "DELETE", Body = Json.Object().Set("token", token).Build() }, ct);
        }
        public void Poll() => _http.Poll();
        public void Dispose() => _http.Dispose();
    }
}
