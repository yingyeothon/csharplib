using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Yingyeothon.Auth;
using Yingyeothon.Codec;
using Yingyeothon.Gamebase.Client;
using Yingyeothon.KvStore;
using Yingyeothon.Platform;

namespace Yingyeothon.GameKit
{
    /// <summary>Unified safe module refusal.</summary>
    public sealed class KitException : Exception
    {
        /// <summary>Creates a module error without retaining untrusted exception text.</summary>
        public KitException(string module, string code) : base("kit " + module + ": " + code) { Module = module; Code = code; }
        /// <summary>Failing module.</summary>
        public string Module { get; }
        /// <summary>Shared cross-language error code.</summary>
        public string Code { get; }
        /// <summary>Whether retry may help.</summary>
        public bool Retryable => Code == "offline" || Code == "conflict" || Code == "unavailable" || Code == "rate_limited" || Code == "inbox_full" || Code == "cooldown";
    }
    /// <summary>Optional secure token persistence owned by the game.</summary>
    public interface IKitTokenStorage
    {
        /// <summary>Loads a stored player token, or null.</summary>
        Task<string?> LoadAsync(CancellationToken cancellationToken);
        /// <summary>Durably stores a token. Never log it.</summary>
        Task StoreAsync(string token, CancellationToken cancellationToken);
        /// <summary>Clears the opted-in storage.</summary>
        Task ClearAsync(CancellationToken cancellationToken);
    }
    /// <summary>Optional durable queue, isolated by endpoint, channel, collection and player.</summary>
    public interface IKitQueueStorage
    {
        /// <summary>Loads the ordered JSON records for one namespace.</summary>
        Task<IReadOnlyList<JsonValue>> LoadAsync(string namespaceId, CancellationToken cancellationToken);
        /// <summary>Atomically and durably replaces the ordered records.</summary>
        Task StoreAsync(string namespaceId, IReadOnlyList<JsonValue> records, CancellationToken cancellationToken);
    }
    /// <summary>System browser/redirect adapter; storage and browser UI remain host-owned.</summary>
    public interface IKitLoginAdapter
    {
        /// <summary>Opens the URL and waits for a redirect; cancellation must close the flow.</summary>
        Task<Uri> OpenAsync(Uri startUrl, CancellationToken cancellationToken);
    }
    /// <summary>Options copied by the kit.</summary>
    public sealed class GameKitOptions
    {
        /// <summary>Service kit-config JSON, retaining its service field names.</summary>
        public JsonValue Config { get; set; } = JsonValue.Null;
        /// <summary>Optional Android topic SDK seam.</summary>
        public IKitPushTopics? PushTopics { get; set; }

        /// <summary>State HTTP transport; use UnityWebRequestTransport for WebGL.</summary>
        public IHttpTransport? Transport { get; set; }
        /// <summary>Auth transport.</summary>
        public IAuthTransport? AuthTransport { get; set; }
        /// <summary>Gateway and match socket factory.</summary>
        public IWebSocketFactory? WebSocketFactory { get; set; }
        /// <summary>Monotonic clock.</summary>
        public IClock? Clock { get; set; }
        /// <summary>Token storage is opt-in.</summary>
        public IKitTokenStorage? TokenStorage { get; set; }
        /// <summary>Durable conditional-save queue is opt-in.</summary>
        public IKitQueueStorage? OfflineQueue { get; set; }
        /// <summary>Registered save-name policies used after queue restoration.</summary>
        public IReadOnlyDictionary<string, Func<JsonValue?, JsonValue?, JsonValue>>? MergePolicies { get; set; }
        /// <summary>System browser adapter.</summary>
        public IKitLoginAdapter? LoginAdapter { get; set; }
        /// <summary>Allowed login return URL.</summary>
        public Uri? RedirectUrl { get; set; }
        /// <summary>Provider override; default kit-config auth.provider, then github.</summary>
        public string? Provider { get; set; }
        /// <summary>Content polling interval, minimum one second.</summary>
        public double ContentPollMillis { get; set; } = 60000;
        /// <summary>Mail and friends polling interval, minimum one second.</summary>
        public double SocialPollMillis { get; set; } = 30000;
        /// <summary>Deferred ticket polling interval, minimum one second.</summary>
        public double MatchPollMillis { get; set; } = 4000;
        /// <summary>Whether the live match channel has a callback; never inferred from result.</summary>
        public bool MatchCallback { get; set; }
    }
    /// <summary>Purpose-shaped game modules. The kit owns and pumps its lower-level clients.</summary>
    public interface IGameKit : IPollable
    {
        /// <summary>Session and platform clock.</summary>
        IKitSession Session { get; }
        /// <summary>Published content.</summary>
        IKitContent Content { get; }
        /// <summary>Conditional player persistence.</summary>
        IKitSave Save { get; }
        /// <summary>Host room and routed events.</summary>
        IKitRoom Room { get; }
        /// <summary>Deterministic turn games.</summary>
        IKitTurns Turns { get; }
        /// <summary>Player inbox.</summary>
        IKitMail Mail { get; }
        /// <summary>Relations and gateway presence.</summary>
        IKitFriends Friends { get; }
        /// <summary>Live and deferred matchmaking.</summary>
        IKitMatchmaking Matchmaking { get; }
        /// <summary>Push registration and messages.</summary>
        IKitPush Push { get; }
        /// <summary>Board by configured alias.</summary>
        IKitBoard Board(string alias);
        /// <summary>Disposes and completes after queued work and cleanup settle; keep polling while awaiting.</summary>
        Task ShutdownAsync();
        /// <summary>Starts or stops background polling; resume schedules immediate refresh.</summary>
        void SetForeground(bool foreground);
    }
    /// <summary>Factory for a kit with engine-free modules.</summary>
    public static class GameKit
    {
        /// <summary>Creates a kit without opening a socket or storing a credential.</summary>
        public static IGameKit Create(GameKitOptions options) => new KitRuntime(options);
    }
    internal sealed partial class KitRuntime : IGameKit
    {
        internal readonly JsonValue Config;
        internal readonly IHttpTransport? Transport;
        internal readonly IClock Clock;
        internal readonly IKitQueueStorage? QueueStorage;
        internal readonly Dictionary<string, Func<JsonValue?, JsonValue?, JsonValue>> Policies;
        internal readonly double ContentInterval, SocialInterval, MatchInterval;
        internal readonly bool MatchCallback;
        internal readonly IWebSocketFactory? SocketFactory;
        internal readonly List<IPollable> Clients = new List<IPollable>();
        internal readonly ConcurrentQueue<Action> Actions = new ConcurrentQueue<Action>();
        private readonly ConcurrentQueue<Action> _settlements = new ConcurrentQueue<Action>();
        internal bool Foreground = true, Disposed;
        internal int Generation;
        private int _activeWork;
        private readonly List<TaskCompletionSource<bool>> _shutdown = new List<TaskCompletionSource<bool>>();
        private int _poll;
        private readonly List<(double At, TaskCompletionSource<bool> Source, CancellationToken Cancel)> _timers = new List<(double, TaskCompletionSource<bool>, CancellationToken)>();
        internal Task Delay(double milliseconds, CancellationToken ct)
        { var source = new TaskCompletionSource<bool>(); _timers.Add((Clock.NowMillis + milliseconds, source, ct)); return source.Task; }
        private readonly KitContext _context;
        internal readonly SessionImpl Identity;
        private readonly ContentImpl _content;
        private readonly SaveImpl _save;
        private readonly RoomImpl _room;
        private readonly MailImpl _mail;
        private readonly FriendsImpl _friends;
        internal readonly MatchmakingImpl MatchImpl;
        private readonly PushImpl _push;
        private readonly TurnsImpl _turns;
        internal KitRuntime(GameKitOptions o)
        {
            if (o == null)
            {
                throw new ArgumentNullException(nameof(o));
            }

            if (o.Config == null || o.Config.Kind != JsonKind.Object)
            {
                throw new ArgumentException("Expected kit config", nameof(o));
            }

            foreach (var interval in new[] { o.ContentPollMillis, o.SocialPollMillis, o.MatchPollMillis })
            {
                if (double.IsNaN(interval) || double.IsInfinity(interval) || interval < 1000)
                {
                    throw new ArgumentException("Polling intervals must be finite and at least 1000 ms", nameof(o));
                }
            }

            Config = o.Config; Transport = o.Transport; Clock = o.Clock ?? SystemClock.Instance; SocketFactory = o.WebSocketFactory;
            QueueStorage = o.OfflineQueue; Policies = o.MergePolicies == null ? new Dictionary<string, Func<JsonValue?, JsonValue?, JsonValue>>() : o.MergePolicies.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
            ContentInterval = o.ContentPollMillis; SocialInterval = o.SocialPollMillis; MatchInterval = o.MatchPollMillis; MatchCallback = o.MatchCallback;
            _context = new KitContext(Actions);
            Identity = new SessionImpl(this, o); _content = new ContentImpl(this); _save = new SaveImpl(this); _room = new RoomImpl(this); _mail = new MailImpl(this); _friends = new FriendsImpl(this); MatchImpl = new MatchmakingImpl(this); _push = new PushImpl(this, o.PushTopics); _turns = new TurnsImpl(this, _room);
        }
        public IKitSession Session => Identity;
        public IKitContent Content => _content;
        public IKitSave Save => _save;
        public IKitRoom Room => _room;
        public IKitTurns Turns => _turns;
        public IKitMail Mail => _mail;
        public IKitFriends Friends => _friends;
        public IKitMatchmaking Matchmaking => MatchImpl;
        public IKitPush Push => _push;
        public IKitBoard Board(string alias) => new BoardImpl(this, Alias("boards", alias));
        internal string Alias(string section, string alias)
        {
            if (!Config.TryGetMember(section, out var aliases) || aliases.GetString(alias) is not string name)
            {
                throw new KitException(section, "not_configured");
            }

            return name;
        }
        internal string Field(string section, string field)
        {
            if (!Config.TryGetMember(section, out var value) || value.GetString(field) is not string text || text.Length == 0)
            {
                throw new KitException(section, "not_configured");
            }

            return text;
        }
        internal void Check(int generation)
        {
            if (Disposed || generation != Generation || Identity.Token == null)
            {
                throw new KitException("session", "signed_out");
            }
        }
        internal IKvNamespace Namespace(string alias, bool owned)
        {
            if (Identity.Token == null)
            {
                throw new KitException(alias, "signed_out");
            }

            var collection = KvStoreClient.Create(new KvStoreClientOptions { BaseUrl = Field("state", "url"), Token = Identity.Token, Transport = Transport }).Collection(Alias("collections", alias));
            return owned ? collection.Mine : collection;
        }
        private readonly Dictionary<string, IPlatformClient> _platforms = new Dictionary<string, IPlatformClient>();
        internal void StopClients(string? priorToken = null)
        {
            var cleanup = _push?.Detach(priorToken);
            foreach (var timer in _timers)
            {
                timer.Source.TrySetCanceled();
            }

            _timers.Clear();
            var draining = new List<IPollable>();
            foreach (var client in Clients.ToArray())
            {
                if (_push?.MustDrain(client) == true)
                {
                    draining.Add(client);
                }
                else { client.Dispose(); client.Poll(); }
            }
            Clients.Clear(); Clients.AddRange(draining); _platforms.Clear(); _content?.Reset(); _save?.Reset(); _mail?.Reset(); _friends?.Reset(); _room?.Close(); MatchImpl?.Reset(); _push?.Reset(); _turns?.Reset();
            if (cleanup != null)
            {
                _ = Run("cleanup", cleanup);
            }
        }
        internal IPlatformClient Platform(string? baseUrl = null)
        {
            var key = baseUrl ?? Field("state", "url");
            if (_platforms.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var c = PlatformClient.Create(new PlatformClientOptions { BaseUrl = baseUrl ?? Field("state", "url"), Token = Identity.Token, Transport = Transport, NowMillis = () => Clock.NowMillis }); Clients.Add(c); _platforms[key] = c; return c;
        }
        internal Task<T> Run<T>(string module, Func<Task<T>> work, CancellationToken ct = default)
        {
            if (Disposed)
            {
                throw new ObjectDisposedException(nameof(IGameKit));
            }

            var source = new TaskCompletionSource<T>();
            Interlocked.Increment(ref _activeWork);
            void Settle(Action complete) { Interlocked.Decrement(ref _activeWork); complete(); }
            Actions.Enqueue(() => Start());


            async void Start()
            {
                T result;
                var generation = Generation;
                try
                {
                    if (Disposed && module != "cleanup")
                    {
                        throw new OperationCanceledException();
                    }

                    ct.ThrowIfCancellationRequested(); result = await work(); ct.ThrowIfCancellationRequested(); if (module != "session" && module != "cleanup")
                    {
                        Check(generation);
                    }
                }
                catch (ArgumentException error) { _settlements.Enqueue(() => Settle(() => source.TrySetException(error))); return; }
                catch (OperationCanceledException) { _settlements.Enqueue(() => Settle(() => source.TrySetCanceled())); return; }
                catch (Exception error)
                {
                    var mapped = Map(module, error);
                    _settlements.Enqueue(() => Settle(() => source.TrySetException(mapped)));
                    if (mapped.Code == "signed_out" && generation == Generation && module != "cleanup")
                    {
                        Identity.Stop();
                    }

                    return;
                }
                _settlements.Enqueue(() => Settle(() => source.TrySetResult(result)));
            }
            return source.Task;
        }
        internal Task Run(string module, Func<Task> work, CancellationToken ct = default) => Run(module, async () => { await work(); return true; }, ct);
        internal static KitException Map(string module, Exception error)
        {
            if (error is KitException k)
            {
                return k;
            }

            var status = error is PlatformException p ? p.Status : error is KvStoreException kv ? kv.Status : 0;
            var reason = error is PlatformException pp ? pp.Reason : error is KvStoreException kk ? kk.Reason : null;
            var code = error is PlatformException cancelled && cancelled.Code == "cancelled" ? "cancelled" : status == 401 ? "signed_out" : status == 403 ? "forbidden" : status == 410 ? "channel_inactive" : reason == "out_of_range" ? "out_of_range"
                : module == "mail" && (reason == "sender_full" || reason == "owner_full" || reason == "collection_full") ? "inbox_full"
                : module == "push" && (status == 400 || status == 409) ? "push_refused" : reason == "cooldown" ? "cooldown" : status == 429 ? "rate_limited" : status == 409 ? "conflict" : "unavailable";
            return new KitException(module, code);
        }
        public void SetForeground(bool foreground) { Actions.Enqueue(() => { Foreground = foreground; if (foreground) { Identity.Resume(); _turns.Resume(); _ = _save.FlushAsync(); _content.Refresh(); _mail.Refresh(); _friends.Refresh(); MatchImpl.Wake(); } }); }
        public void Poll()
        {
            if (Interlocked.Exchange(ref _poll, 1) != 0)
            {
                throw new InvalidOperationException("Poll is not reentrant");
            }



            var previous = SynchronizationContext.Current; SynchronizationContext.SetSynchronizationContext(_context);
            try
            {
                foreach (var timer in _timers.ToArray()) { if (timer.Cancel.IsCancellationRequested) { _timers.Remove(timer); timer.Source.TrySetCanceled(); } else if (Clock.NowMillis >= timer.At) { _timers.Remove(timer); timer.Source.TrySetResult(true); } }
                foreach (var c in Clients.ToArray())
                {
                    c.Poll();
                }

                for (var i = 0; i < 512 && Actions.TryDequeue(out var action); i++)
                {
                    action();
                }

                if (!Disposed) { Identity.Tick(); _room.Tick(); _turns.Tick(); if (Foreground && Identity.Token != null) { _content.Tick(); _mail.Tick(); _friends.Tick(); MatchImpl.Tick(); } }
                if (Disposed && _activeWork == 0 && Actions.IsEmpty)
                {
                    var shutdowns = _shutdown.ToArray(); _shutdown.Clear(); foreach (var shutdown in shutdowns)
                    {
                        shutdown.TrySetResult(true);
                    }
                }
            }
            finally
            {
                try
                {
                    for (var i = 0; i < 512 && _settlements.TryDequeue(out var settle); i++)
                    {
                        settle();
                    }
                }
                finally { SynchronizationContext.SetSynchronizationContext(previous); Volatile.Write(ref _poll, 0); }
            }
        }
        public Task ShutdownAsync() { var source = new TaskCompletionSource<bool>(); Actions.Enqueue(() => _shutdown.Add(source)); Dispose(); return source.Task; }
        public void Dispose() { Actions.Enqueue(() => { if (Disposed) { return; } Identity.Stop(false); Disposed = true; _room.Close(); }); }
        private sealed class KitContext : SynchronizationContext
        {
            private readonly ConcurrentQueue<Action> _queue;
            internal KitContext(ConcurrentQueue<Action> queue) { _queue = queue; }
            public override void Post(SendOrPostCallback callback, object? state) => _queue.Enqueue(() => callback(state));
        }
    }
}
