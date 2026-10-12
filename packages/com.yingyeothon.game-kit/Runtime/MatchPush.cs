using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Yingyeothon.Codec;
using Yingyeothon.Match;
using Yingyeothon.Platform;
using Yingyeothon.Push;

namespace Yingyeothon.GameKit
{
    /// <summary>Topic SDK seam owned by an Android host.</summary>
    public interface IKitPushTopics
    {
        /// <summary>Subscribes to the exact public topic from kit config.</summary>
        Task SubscribeAsync(string topic, CancellationToken cancellationToken);
        /// <summary>Unsubscribes on sign-out.</summary>
        Task UnsubscribeAsync(string topic, CancellationToken cancellationToken);
    }
    /// <summary>Push lifecycle and match wakeups. Topic messages grant no entitlement.</summary>
    public interface IKitPush
    {
        /// <summary>Registers a device and subscribes after a successful registration.</summary>
        Task RegisterAsync(PushRegisterOptions options, CancellationToken cancellationToken = default);
        /// <summary>Deletes the last registered token and unsubscribes.</summary>
        Task UnregisterAsync(CancellationToken cancellationToken = default);
        /// <summary>Queues a Firebase data message or notification-tap payload for Poll.</summary>
        void Receive(IReadOnlyDictionary<string, string> data);
        /// <summary>Campaign, targeted or broadcast payload.</summary>
        event Action<IReadOnlyDictionary<string, string>> MessageReceived;
    }
    /// <summary>Live and deferred matchmaking policy.</summary>
    public interface IKitMatchmaking
    {
        /// <summary>Live find plus callback-less room formation; deferred mode uses SubmitAsync instead.</summary>
        Task<MatchResult> FindAsync(CancellationToken cancellationToken = default);
        /// <summary>Submits a deferred ticket.</summary>
        Task<MatchTicket> SubmitAsync(CancellationToken cancellationToken = default);
        /// <summary>Reads authoritative deferred status.</summary>
        Task<MatchTicket?> StatusAsync(CancellationToken cancellationToken = default);
        /// <summary>Cancels live find or deletes a deferred ticket.</summary>
        Task CancelAsync(CancellationToken cancellationToken = default);
        /// <summary>Accepts a proposal.</summary>
        Task<MatchTicket> AcceptAsync(CancellationToken cancellationToken = default);
        /// <summary>Declines a proposal.</summary>
        Task<MatchTicket> DeclineAsync(CancellationToken cancellationToken = default);
        /// <summary>Observed deferred state transition.</summary>
        event Action<MatchTicket> StateChanged;
    }
    internal sealed class MatchmakingImpl : IKitMatchmaking
    {
        private readonly KitRuntime _r; private IDeferredMatchClient? _deferred; private ILiveMatchClient? _live; private bool _busy, _active; private double _next; private string? _last; private HashSet<string>? _matched; private double _inviteUntil;


        internal MatchmakingImpl(KitRuntime r) { _r = r; r.Room.Invited += invite => { if (_matched?.Contains(invite.From) == true && r.Clock.NowMillis <= _inviteUntil) { _ = r.Room.AcceptAsync(invite.PartyId); } }; }
        public event Action<MatchTicket>? StateChanged;
        private bool Deferred => _r.Config.TryGetMember("match", out var match) && match.GetString("mode") == "deferred";
        private IDeferredMatchClient Client()
        {
            if (!Deferred)
            {
                throw new KitException("matchmaking", "invalid_state");
            }

            if (_deferred == null) { _deferred = DeferredMatchClient.Create(new DeferredMatchClientOptions { ChannelId = _r.Field("match", "channelId"), Http = new PlatformClientOptions { BaseUrl = _r.Field("match", "apiBase"), Token = _r.Identity.Token, Transport = _r.Transport, NowMillis = () => _r.Clock.NowMillis } }); _r.Clients.Add(_deferred); }
            return _deferred;
        }
        public Task<MatchResult> FindAsync(CancellationToken ct = default) => _r.Run("matchmaking", async () =>
        {


            if (Deferred)
            {
                throw new KitException("matchmaking", "invalid_state");
            }

            var room = (RoomImpl)_r.Room; if (!room.Connected)
            {
                throw new KitException("matchmaking", "lobby_required");
            }

            var generation = _r.Generation;
            if (_live == null) { _live = LiveMatchClient.Create(new LiveMatchClientOptions { Url = _r.Field("match", "url"), ChannelId = _r.Field("match", "channelId"), Token = _r.Identity.Token!, WebSocketFactory = _r.SocketFactory, Clock = _r.Clock }); _r.Clients.Add(_live); }
            var result = await _live.FindAsync(ct); _r.Check(generation); if (_r.MatchCallback)
            {
                return result;


            }

            _matched = new HashSet<string>(result.Members); _inviteUntil = _r.Clock.NowMillis + 30000;
            if (result.Members.OrderBy(x => x, StringComparer.Ordinal).FirstOrDefault() == _r.Identity.UserId)
            {
                await room.CreateAsync(ct); foreach (var id in result.Members)
                {
                    if (id != _r.Identity.UserId)
                    {
                        await room.InviteAsync(id, ct);
                    }
                }
            }
            await room.WaitForMembers(_matched, _inviteUntil, ct); _r.Check(generation);
            var partial = result.Partial || result.Members.Any(id => !room.Members.Any(x => x.UserId == id)); _matched = null;
            var b = Json.Object(); foreach (var m in result.Raw.AsObject())
            {
                if (m.Key != "partial")
                {
                    b.Set(m.Key, m.Value);
                }
            }

            b.Set("partial", partial); return new MatchResult(b.Build());
        }, ct);
        private void Observe(MatchTicket? ticket)
        {
            if (ticket == null) { _active = false; return; }
            var signature = Json.Stringify(ticket.Raw);
            _active = ticket.State == MatchTicketState.Waiting || ticket.State == MatchTicketState.Proposed;
            if (signature != _last) { _last = signature; StateChanged?.Invoke(ticket); }
        }
        private Task<MatchTicket> Command(Func<IDeferredMatchClient, Task<MatchTicket>> command, CancellationToken ct) => _r.Run("matchmaking", async () =>
        { var generation = _r.Generation; MatchTicket result; var attempt = 0; while (true) { try { result = await command(Client()); break; } catch (PlatformException e) when (e.Reason == "cooldown" || e.Reason == "confirming" || e.Status == 503 || e.Status == 429) { await _r.Delay(e.RetryAfterMillis ?? (e.Reason == "confirming" ? 1500 : Math.Min(30000, 1000 * Math.Pow(2, attempt++))), ct); _r.Check(generation); } catch (PlatformException e) when (e.Reason == "proposed" || e.Reason == "proposal_closed" || e.Reason == "not_proposed" || e.Reason == "confirmed") { result = await Client().StatusAsync(ct) ?? throw new KitException("matchmaking", "proposal_closed"); break; } } _r.Check(generation); Observe(result); _next = _r.Clock.NowMillis + _r.MatchInterval; return result; }, ct);
        public Task<MatchTicket> SubmitAsync(CancellationToken ct = default) => Command(c => c.SubmitAsync(ct), ct);
        public Task<MatchTicket> AcceptAsync(CancellationToken ct = default) => Command(c => c.AcceptAsync(ct), ct);
        public Task<MatchTicket> DeclineAsync(CancellationToken ct = default) => Command(c => c.DeclineAsync(ct), ct);
        public Task<MatchTicket?> StatusAsync(CancellationToken ct = default) => _r.Run("matchmaking", async () => { var generation = _r.Generation; var t = await Client().StatusAsync(ct); _r.Check(generation); Observe(t); return t; }, ct);
        public Task CancelAsync(CancellationToken ct = default) => _r.Run("matchmaking", async () =>
        {
            if (Deferred)
            {
                await Client().CancelAsync(ct);
            }
            else
            {
                _live?.Cancel();
            }
            _active = false; _matched = null;
        }, ct);
        internal void Wake()
        {
            _next = 0; if (Deferred)
            {
                _active = true;
            }
        }
        internal void Reset() { _deferred = null; _live = null; _last = null; _active = false; _busy = false; _matched = null; }
        internal void Tick()
        {
            if (!_active || _busy || _r.Clock.NowMillis < _next)
            {
                return;
            }

            _busy = true; _next = _r.Clock.NowMillis + _r.MatchInterval;
            _ = _r.Run("matchmaking", async () => { try { var generation = _r.Generation; var t = await Client().StatusAsync(); _r.Check(generation); Observe(t); } catch (PlatformException e) { _next = _r.Clock.NowMillis + (e.RetryAfterMillis ?? (e.Reason == "confirming" ? 1500 : Math.Min(30000, _r.MatchInterval * 2))); throw; } finally { _busy = false; } });
        }
    }
    internal sealed class PushImpl : IKitPush
    {
        private Task _cleanupTail = Task.CompletedTask;
        private readonly object _lifecycleLock = new object();
        private readonly HashSet<IPollable> _draining = new HashSet<IPollable>();
        private readonly SemaphoreSlim _topicsLock = new SemaphoreSlim(1, 1);
        internal bool MustDrain(IPollable client) => _draining.Contains(client);
        private (Task Previous, TaskCompletionSource<bool> Finished) Reserve()
        {
            lock (_lifecycleLock) { var previous = _cleanupTail; var finished = new TaskCompletionSource<bool>(); _cleanupTail = finished.Task; return (previous, finished); }
        }
        private static async Task CompleteReservation(Task operation, Task previous, TaskCompletionSource<bool> finished)
        {
            try { await previous; try { await operation; } catch (Exception) { } }
            finally { finished.TrySetResult(true); }
        }
        private Task Lifecycle(Func<Task> operation, CancellationToken ct)
        {
            var reservation = Reserve(); Task work;
            try { work = _r.Run("push", async () => { var generation = _r.Generation; await reservation.Previous; _r.Check(generation); await operation(); }, ct); }
            catch { reservation.Finished.TrySetResult(true); throw; }
            _r.Actions.Enqueue(() => { _ = CompleteReservation(work, reservation.Previous, reservation.Finished); }); return work;
        }
        private void Release(IPushClient client)
        {
            _draining.Remove(client);
            if (_client != client) { client.Dispose(); client.Poll(); _r.Clients.Remove(client); }
        }
        private int _queued;
        private readonly KitRuntime _r; private readonly IKitPushTopics? _topics; private IPushClient? _client; private string? _token; private int _registeredGeneration = -1; private int? _topicGeneration;
        internal PushImpl(KitRuntime r, IKitPushTopics? topics) { _r = r; _topics = topics; }
        public event Action<IReadOnlyDictionary<string, string>>? MessageReceived;
        private IPushClient Client()
        { if (_client == null) { _client = PushClient.Create(new PushClientOptions { ChannelId = _r.Field("push", "channelId"), Http = new PlatformClientOptions { BaseUrl = _r.Field("push", "apiBase"), Token = _r.Identity.Token, Transport = _r.Transport, NowMillis = () => _r.Clock.NowMillis } }); _r.Clients.Add(_client); } return _client; }
        public Task RegisterAsync(PushRegisterOptions options, CancellationToken ct = default)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            var copy = new PushRegisterOptions { Token = options.Token, Project = options.Project };
            return Lifecycle(async () =>
            {
                var generation = _r.Generation; var client = Client();
                ct.ThrowIfCancellationRequested();
                var registering = client.RegisterAsync(copy, CancellationToken.None);
                _token = copy.Token; _registeredGeneration = generation; _draining.Add(client);
                try
                {
                    await registering; _r.Check(generation);
                    if (_topics != null)
                    {
                        await _topicsLock.WaitAsync(ct);
                        try { _r.Check(generation); await _topics.SubscribeAsync(_r.Field("push", "topic"), ct); _topicGeneration = generation; }
                        finally { _topicsLock.Release(); }
                    }
                    _r.Check(generation);
                }
                finally { Release(client); }
            }, ct);
        }
        private async Task Unsubscribe(int generation, string topic)
        {
            if (_topics == null)
            {
                return;
            }

            await _topicsLock.WaitAsync(CancellationToken.None);
            try
            {
                if (_topicGeneration == generation) { await _topics.UnsubscribeAsync(topic, CancellationToken.None); _topicGeneration = null; }
            }
            finally { _topicsLock.Release(); }
        }
        public Task UnregisterAsync(CancellationToken ct = default) => Lifecycle(async () =>
        {
            var device = _token; var generation = _registeredGeneration;
            if (device == null)
            {
                return;
            }

            var topic = _r.Field("push", "topic"); var client = Client(); _draining.Add(client);
            try { ct.ThrowIfCancellationRequested(); await client.UnregisterAsync(device, CancellationToken.None); }
            finally
            {
                try { await Unsubscribe(generation, topic); }
                finally { if (_registeredGeneration == generation && _token == device) { _token = null; _registeredGeneration = -1; } Release(client); }
            }
        }, ct);
        internal Func<Task>? Detach(string? jwt)
        {
            var device = _token; var generation = _registeredGeneration; _token = null; _registeredGeneration = -1;
            if (device == null || jwt == null)
            {
                return null;
            }

            var channel = _r.Field("push", "channelId"); var origin = _r.Field("push", "apiBase"); var topic = _r.Field("push", "topic"); var reservation = Reserve();
            return async () =>
            {
                await reservation.Previous;
                IPushClient? client = null;
                try
                {
                    client = PushClient.Create(new PushClientOptions { ChannelId = channel, Http = new PlatformClientOptions { BaseUrl = origin, Token = jwt, Transport = _r.Transport, NowMillis = () => _r.Clock.NowMillis } }); _r.Clients.Add(client); _draining.Add(client);
                    try { await client.UnregisterAsync(device, CancellationToken.None); } finally { await Unsubscribe(generation, topic); }
                }
                finally { try { if (client != null) { Release(client); } } finally { reservation.Finished.TrySetResult(true); } }
            };
        }
        public void Receive(IReadOnlyDictionary<string, string> data)
        {
            if (data == null) { throw new ArgumentNullException(nameof(data)); }
            if (data.Count > 64 || data.Sum(x => x.Key.Length + x.Value.Length) > 8192) { throw new ArgumentException("Push data exceeds bounds", nameof(data)); }
            if (Interlocked.Increment(ref _queued) > 128) { Interlocked.Decrement(ref _queued); return; }
            var copy = data.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal); _r.Actions.Enqueue(() =>
            {
                Interlocked.Decrement(ref _queued); if (PushClient.Classify(copy) == PushMessageKind.Match && copy.TryGetValue("channelId", out var channel) && _r.Config.TryGetMember("match", out var match) && channel == match.GetString("channelId"))
                {
                    _r.MatchImpl.Wake();
                }
                else
                {
                    MessageReceived?.Invoke(copy);
                }
            });
        }
        internal void Reset() { _client = null; _token = null; }
    }
}
