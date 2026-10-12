using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Yingyeothon.Codec;
using Yingyeothon.Gamebase.Client;

namespace Yingyeothon.GameKit
{
    /// <summary>One routed game event with its authenticated gateway sender.</summary>
    public sealed class KitRoomEvent
    {
        /// <summary>Creates a routed event.</summary>
        public KitRoomEvent(string name, string from, JsonValue data) { Name = name; From = from; Data = data; }
        /// <summary>User event name.</summary>
        public string Name { get; }
        /// <summary>Authenticated user id.</summary>
        public string From { get; }
        /// <summary>Event data.</summary>
        public JsonValue Data { get; }
    }
    /// <summary>Private room mirroring the gateway party.</summary>
    public interface IKitRoom
    {
        /// <summary>Current room id.</summary>
        string? Id { get; }
        /// <summary>Current leader id.</summary>
        string? Host { get; }
        /// <summary>Whether this player is leader.</summary>
        bool IsHost { get; }
        /// <summary>Latest members.</summary>
        IReadOnlyList<PartyMember> Members { get; }
        /// <summary>Latest accepted host snapshot.</summary>
        JsonValue? LastKnown { get; }
        /// <summary>Connects the lobby without creating a party.</summary>
        Task ConnectAsync(CancellationToken cancellationToken = default);
        /// <summary>Connects the lobby and creates a room, completing on its roster.</summary>
        Task CreateAsync(CancellationToken cancellationToken = default);
        /// <summary>Invites a player.</summary>
        Task InviteAsync(string userId, CancellationToken cancellationToken = default);
        /// <summary>Accepts an invitation.</summary>
        Task AcceptAsync(string roomId, CancellationToken cancellationToken = default);
        /// <summary>Declines an invitation.</summary>
        Task DeclineAsync(string roomId, CancellationToken cancellationToken = default);
        /// <summary>Leaves the current room.</summary>
        Task LeaveAsync(CancellationToken cancellationToken = default);
        /// <summary>Routes a user event with a bounded kit envelope.</summary>
        Task SendAsync(string name, JsonValue data, CancellationToken cancellationToken = default);
        /// <summary>Routes a private user event.</summary>
        Task WhisperAsync(string userId, string name, JsonValue data, CancellationToken cancellationToken = default);
        /// <summary>Publishes a host snapshot; larger states use game-defined deltas.</summary>
        Task PublishAsync(JsonValue snapshot, CancellationToken cancellationToken = default);
        /// <summary>Host changed on the pump.</summary>
        event Action<string> HostChanged;
        /// <summary>New host hook with the previous host's last accepted snapshot.</summary>
        event Action<JsonValue?> BecomeHost;
        /// <summary>Accepted host state.</summary>
        event Action<JsonValue> StateReceived;
        /// <summary>User-defined events, excluding reserved kit names.</summary>
        event Action<KitRoomEvent> EventReceived;
        /// <summary>Incoming invite; accept or decline through this room.</summary>
        event Action<PartyInviteFrame> Invited;
        /// <summary>Asynchronous gateway command refusal, including send-only event operations.</summary>
        event Action<KitException> Refused;
        /// <summary>A member joined.</summary>
        event Action<PartyMember> MemberJoined;
        /// <summary>A member left.</summary>
        event Action<PartyMember> MemberLeft;
        /// <summary>A member's online state changed.</summary>
        event Action<PartyMember> MemberOnline;
    }
    internal sealed class RoomImpl : IKitRoom
    {
        private readonly KitRuntime _r; private IGatewayLobbyClient? _lobby;
        private readonly string _senderSession = Auth.AuthClient.NewNonce(); private long _seq;
        private readonly Dictionary<string, long> _seen = new Dictionary<string, long>(); private string? _hostSession; private readonly HashSet<string> _retiredHosts = new HashSet<string>(); private long _revision; private long _accepted = -1;
        private IReadOnlyList<PartyMember> _members = Array.Empty<PartyMember>();
        private readonly List<(Func<bool> Ready, TaskCompletionSource<bool> Source, double Deadline, CancellationToken Cancel)> _waits = new List<(Func<bool>, TaskCompletionSource<bool>, double, CancellationToken)>();
        internal RoomImpl(KitRuntime r) { _r = r; }
        public string? Id { get; private set; }
        public string? Host { get; private set; }
        public bool IsHost => Id != null && Host == _r.Identity.UserId;
        public IReadOnlyList<PartyMember> Members => _members; public JsonValue? LastKnown { get; private set; }
        public event Action<string>? HostChanged; public event Action<JsonValue?>? BecomeHost; public event Action<JsonValue>? StateReceived; public event Action<KitRoomEvent>? EventReceived; public event Action<PartyInviteFrame>? Invited;
        public event Action<PartyMember>? MemberJoined; public event Action<PartyMember>? MemberLeft; public event Action<PartyMember>? MemberOnline;
        public event Action<KitException>? Refused;
        internal event Action<string, string, JsonValue>? InternalEvent;
        internal event Action? Reconnected;
        internal async Task Connect(CancellationToken ct)
        {
            if (_lobby == null)
            {
                _r.Check(_r.Generation);
                _lobby = GatewayLobbyClient.Create(new GatewayLobbyClientOptions { Url = _r.Field("gateway", "url"), ChannelId = _r.Field("gateway", "lobbyChannelId"), Token = _r.Identity.Token!, Clock = _r.Clock, WebSocketFactory = _r.SocketFactory });
                _lobby.Refused += error => { var refusal = new KitException("room", error.Code == "unknown_user" ? "offline" : error.Code == "forbidden" ? "forbidden" : "invalid_state"); foreach (var wait in _waits.ToArray()) { _waits.Remove(wait); wait.Source.TrySetException(refusal); } Refused?.Invoke(refusal); };
                _lobby.PartyChanged += Roster; _lobby.PartyInvited += f => Invited?.Invoke(f); _lobby.EventReceived += Receive;


                _lobby.Connected += _ => { Reconnected?.Invoke(); if (Id != null && !IsHost) { SendInternal("kit.resync", JsonValue.Null, Host); } };
            }
            if (_lobby.State != GatewayClientState.Connected)
            {
                await _lobby.ConnectAsync();
            }

            ct.ThrowIfCancellationRequested();
        }
        internal bool Connected => _lobby?.State == GatewayClientState.Connected;
        public Task ConnectAsync(CancellationToken ct = default) => _r.Run("room", () => Connect(ct), ct);
        public Task CreateAsync(CancellationToken ct = default) => _r.Run("room", async () => { await Connect(ct); _lobby!.Party.Create(); await Wait(() => Id != null, ct); }, ct);
        public Task InviteAsync(string id, CancellationToken ct = default) => _r.Run("room", async () => { await Connect(ct); _lobby!.Party.Invite(id); await Wait(() => _lobby.Roster?.Invited.Contains(id) == true, ct); }, ct);
        public Task AcceptAsync(string id, CancellationToken ct = default) => _r.Run("room", async () => { await Connect(ct); _lobby!.Party.Accept(id); await Wait(() => Id == id, ct); }, ct);
        public Task DeclineAsync(string id, CancellationToken ct = default) => _r.Run("room", async () => { await Connect(ct); _lobby!.Party.Decline(id); }, ct);
        public Task LeaveAsync(CancellationToken ct = default) => _r.Run("room", async () => { await Connect(ct); _lobby!.Party.Leave(); await Wait(() => Id == null, ct); }, ct);
        public Task SendAsync(string name, JsonValue data, CancellationToken ct = default) => _r.Run("room", () => { UserName(name); SendInternal(name, data); return Task.CompletedTask; }, ct);
        public Task WhisperAsync(string id, string name, JsonValue data, CancellationToken ct = default) => _r.Run("room", () => { UserName(name); SendInternal(name, data, id); return Task.CompletedTask; }, ct);


        private static void UserName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.StartsWith("kit.", StringComparison.Ordinal))
            {
                throw new ArgumentException("User names cannot use kit prefix", nameof(name));
            }
        }
        public Task PublishAsync(JsonValue snapshot, CancellationToken ct = default) => _r.Run("room", () =>
        { if (!IsHost) { throw new KitException("room", "forbidden"); } SendInternal("kit.state", Json.Object().Set("revision", (double)(_revision + 1)).Set("snapshot", snapshot).Build()); _revision++; LastKnown = snapshot; return Task.CompletedTask; }, ct);
        internal void SendInternal(string name, JsonValue data, string? to = null)
        {


            if (Id == null || _lobby == null || !Connected)
            {
                throw new KitException("room", "offline");
            }

            var envelope = Json.Object().Set("v", 1).Set("roomId", Id).Set("senderSession", _senderSession).Set("seq", (double)(_seq + 1)).Set("data", data).Build();
            if (Encoding.UTF8.GetByteCount(Json.Stringify(envelope)) > 8192)
            {
                throw new KitException("room", "payload_too_large");


            }

            _lobby.Event(to == null ? SayScope.Party : SayScope.User, name, envelope, to); _seq++;
        }
        internal Task WaitForMembers(HashSet<string> members, double deadline, CancellationToken ct)
        { var s = new TaskCompletionSource<bool>(); _waits.Add((() => members.All(id => Members.Any(x => x.UserId == id)) || _r.Clock.NowMillis >= deadline, s, deadline + 1, ct)); return s.Task; }
        private Task Wait(Func<bool> ready, CancellationToken ct)


        { if (ready()) { return Task.CompletedTask; } var s = new TaskCompletionSource<bool>(); _waits.Add((ready, s, _r.Clock.NowMillis + 30000, ct)); return s.Task; }
        private void Roster(PartyFrame frame)
        {
            var old = _members; var previous = Host; if (Id != frame.PartyId)

            { LastKnown = null; _seen.Clear(); }
            Id = frame.PartyId; Host = string.IsNullOrEmpty(frame.LeaderId) ? null : frame.LeaderId; _members = frame.Members;
            if (previous != Host) { _accepted = -1; _revision = 0; _hostSession = null; _retiredHosts.Clear(); }
            foreach (var member in _members)
            {
                var prior = old.FirstOrDefault(x => x.UserId == member.UserId); if (prior == null)
                {
                    MemberJoined?.Invoke(member);
                }
                else if (prior.Online != member.Online)
                {
                    MemberOnline?.Invoke(member);
                }
            }
            foreach (var member in old)
            {
                if (!_members.Any(x => x.UserId == member.UserId))
                {
                    MemberLeft?.Invoke(member);
                }
            }

            if (previous != Host && Host != null)
            {
                HostChanged?.Invoke(Host); if (IsHost)
                {
                    BecomeHost?.Invoke(LastKnown);
                }
                else
                {
                    SendInternal("kit.resync", JsonValue.Null, Host);
                }
            }
        }
        private void Receive(EventBroadcastFrame frame)
        {
            if (!_members.Any(x => x.UserId == frame.From))
            {
                return;


            }

            var v = frame.Payload; if (v == null || v.GetNumber("v") != 1 || v.GetString("roomId") != Id || !v.TryGetMember("data", out var data))
            {
                return;
            }

            var session = v.GetString("senderSession"); var seq = v.GetNumber("seq"); if (string.IsNullOrEmpty(session) || session!.Length > 128 || seq == null || seq < 1 || seq > 9007199254740991d || seq != Math.Floor(seq.Value))
            {
                return;
            }

            var key = frame.From + "|" + session; if (_seen.TryGetValue(key, out var last) && seq <= last)
            {
                return;
            }

            if (_seen.Count >= 1024 && !_seen.ContainsKey(key))
            {
                return;
            }

            _seen[key] = (long)seq;
            if (frame.Name == "kit.resync") { if (IsHost && _members.Any(x => x.UserId == frame.From) && LastKnown != null) { SendInternal("kit.state", Json.Object().Set("revision", (double)_revision).Set("snapshot", LastKnown).Build(), frame.From); } return; }
            if (frame.Name == "kit.state")
            {
                if (frame.From != Host)
                {
                    return;


                }

                if (_retiredHosts.Contains(session))
                {
                    return;
                }

                if (_hostSession != session) { if (_hostSession != null) { _retiredHosts.Add(_hostSession); } _hostSession = session; _accepted = -1; }
                var revision = data.GetNumber("revision"); if (revision == null || revision < 0 || revision != Math.Floor(revision.Value) || revision <= _accepted || !data.TryGetMember("snapshot", out var snapshot))
                {
                    return;
                }

                _accepted = (long)revision; LastKnown = snapshot; StateReceived?.Invoke(snapshot); return;
            }


            if (frame.Name.StartsWith("kit.", StringComparison.Ordinal))
            {
                InternalEvent?.Invoke(frame.Name, frame.From, data);
            }
            else
            {
                EventReceived?.Invoke(new KitRoomEvent(frame.Name, frame.From, data));
            }
        }
        internal void Tick()
        {
            _lobby?.Poll(); foreach (var w in _waits.ToArray())
            { if (w.Cancel.IsCancellationRequested) { _waits.Remove(w); w.Source.TrySetCanceled(); } else if (w.Ready()) { _waits.Remove(w); w.Source.TrySetResult(true); } else if (_r.Clock.NowMillis >= w.Deadline) { _waits.Remove(w); w.Source.TrySetException(new KitException("room", "unavailable")); } }
        }


        internal void Close() { var c = _lobby; _lobby = null; c?.Dispose(); Id = null; Host = null; _members = Array.Empty<PartyMember>(); LastKnown = null; _seen.Clear(); foreach (var w in _waits) { w.Source.TrySetException(new KitException("room", "signed_out")); } _waits.Clear(); }
    }
}
