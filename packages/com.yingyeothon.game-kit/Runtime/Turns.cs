using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Yingyeothon.Codec;

namespace Yingyeothon.GameKit
{
    /// <summary>Deterministic turn game input.</summary>
    public sealed class KitTurnsOptions
    {
        /// <summary>Initial JSON state.</summary>
        public JsonValue Initial { get; set; } = JsonValue.Null;
        /// <summary>Pure state reducer; throws to reject a move.</summary>
        public Func<JsonValue, JsonValue, string, JsonValue>? Apply { get; set; }
        /// <summary>Player order, default room members.</summary>
        public IReadOnlyList<string>? Players { get; set; }
        /// <summary>Persist verified logs into the player's save namespace.</summary>
        public bool Persist { get; set; }
        /// <summary>Log TTL seconds, default seven days.</summary>
        public int LogTtlSeconds { get; set; } = 604800;
        /// <summary>Maximum assembled log bytes, default 1 MiB.</summary>
        public int MaxLogBytes { get; set; } = 1048576;
    }
    /// <summary>Deterministic game controlled by the room host.</summary>
    public interface IKitTurnGame
    {
        /// <summary>Random game id announced by the host.</summary>
        string GameId { get; }
        /// <summary>Current state after validated turns.</summary>
        JsonValue State { get; }
        /// <summary>Number of applied turns.</summary>
        int Index { get; }
        /// <summary>Whether a mismatch froze this game.</summary>
        bool Frozen { get; }
        /// <summary>Informational per-player elapsed milliseconds.</summary>
        IReadOnlyDictionary<string, double> Elapsed { get; }
        /// <summary>Submits on this player's turn.</summary>
        Task SubmitAsync(JsonValue move, CancellationToken cancellationToken = default);
        /// <summary>Requests an atomic verified log resynchronization.</summary>
        Task ResyncAsync(CancellationToken cancellationToken = default);
        /// <summary>Host announces a result; each player saves its own result.</summary>
        Task FinishAsync(JsonValue result, CancellationToken cancellationToken = default);
        /// <summary>A validated turn changed State.</summary>
        event Action<JsonValue> Turn;
        /// <summary>Retryable persistence failure, reported separately from mismatch.</summary>
        event Action<KitException> PersistenceFailed;
        /// <summary>A mismatch froze the game.</summary>
        event Action Mismatch;
    }
    /// <summary>Turn module; every peer supplies identical initial state and reducer.</summary>
    public interface IKitTurns
    {
        /// <summary>Prepares a game; the host announces it and peers wait for its announcement.</summary>
        Task<IKitTurnGame> StartAsync(KitTurnsOptions options, CancellationToken cancellationToken = default);
    }
    internal sealed class TurnsImpl : IKitTurns
    {
        private readonly KitRuntime _r; private readonly RoomImpl _room; private TurnGameImpl? _game;


        internal TurnsImpl(KitRuntime r, RoomImpl room) { _r = r; _room = room; room.InternalEvent += Receive; room.HostChanged += host => { if (_game != null) { _ = _game.ResyncAsync(); } }; room.Reconnected += () => { if (_game != null) { _ = _game.ResyncAsync(); } }; }
        public Task<IKitTurnGame> StartAsync(KitTurnsOptions options, CancellationToken ct = default) => _r.Run<IKitTurnGame>("turns", async () =>
        {
            if (options == null || options.Apply == null)


            {
                throw new ArgumentException("A deterministic reducer is required", nameof(options));


            }

            if (options.MaxLogBytes < 8192 || options.MaxLogBytes > 16 * 1024 * 1024 || options.LogTtlSeconds < 1 || options.LogTtlSeconds > 31622400)
            {
                throw new ArgumentException("Invalid log bounds", nameof(options));
            }

            if (_room.Id == null)
            {
                throw new KitException("turns", "invalid_state");
            }

            _game?.Stop();
            _game = new TurnGameImpl(_r, _room, options);
            await _game.Restore(ct); if (_room.IsHost)
            {
                _game.Announce();
            }
            else
            {
                await _game.ResyncAsync(ct);
            }

            return _game;
        }, ct);
        private void Receive(string name, string from, JsonValue data) => _game?.Receive(name, from, data);
        internal void Resume() => _game?.Resume();
        internal void Tick() => _game?.Tick();
        internal void Reset() { _game?.Stop(); _game = null; }
    }
    internal sealed class TurnGameImpl : IKitTurnGame
    {
        private const string Zero = "0000000000000000000000000000000000000000000000000000000000000000";
        private readonly KitRuntime _r; private readonly RoomImpl _room; private readonly JsonValue _initial; private readonly Func<JsonValue, JsonValue, string, JsonValue> _apply;
        private readonly string[] _players; private readonly bool _persist; private readonly int _ttl, _max;
        private readonly List<JsonValue> _records = new List<JsonValue>(); private readonly Dictionary<string, double> _elapsed = new Dictionary<string, double>();
        private readonly Queue<(string Name, JsonValue Data, string To)> _outgoing = new Queue<(string, JsonValue, string)>();
        private readonly List<JsonValue> _buffer = new List<JsonValue>(); private readonly Dictionary<int, JsonValue> _parts = new Dictionary<int, JsonValue>();
        private TaskCompletionSource<bool>? _start; private TaskCompletionSource<bool>? _resync; private string? _request, _responder; private JsonValue? _manifest;
        private double _deadline, _sendAt, _turnAt, _fallbackAt; private bool _fallback; private string _hash = Zero; private int _bytes; private JsonValue? _finish; private JsonValue? _verifiedFinish; private readonly int _generation; private bool _persistBusy, _persistPending;
        internal TurnGameImpl(KitRuntime r, RoomImpl room, KitTurnsOptions o)
        {
            _r = r; _generation = r.Generation; _room = room; _initial = o.Initial ?? throw new ArgumentNullException(nameof(o.Initial)); State = _initial; _apply = o.Apply!;
            _players = (o.Players ?? room.Members.Select(x => x.UserId).ToArray()).ToArray(); if (_players.Length == 0 || _players.Distinct().Count() != _players.Length)
            {
                throw new ArgumentException("Unique player order required");


            }

            _persist = o.Persist; _ttl = o.LogTtlSeconds; _max = o.MaxLogBytes; _turnAt = r.Clock.NowMillis; foreach (var p in _players)
            {
                _elapsed[p] = 0;
            }
        }
        public string GameId { get; private set; } = ""; public JsonValue State { get; private set; }
        public int Index => _records.Count; public bool Frozen { get; private set; }
        public IReadOnlyDictionary<string, double> Elapsed => new System.Collections.ObjectModel.ReadOnlyDictionary<string, double>(_elapsed);
        public event Action<JsonValue>? Turn; public event Action? Mismatch; public event Action<KitException>? PersistenceFailed; private bool _retryOnResume;
        internal void Announce()


        { if (GameId.Length == 0) { GameId = Auth.AuthClient.NewNonce(); } _room.SendInternal("kit.turns.start", Json.Object().Set("gameId", GameId).Set("players", JsonValue.Array(_players.Select(JsonValue.Of))).Set("initialHash", KitJson.Hash(_initial)).Build()); }
        internal Task WaitStart(CancellationToken ct)
        { _start = new TaskCompletionSource<bool>(); _deadline = _r.Clock.NowMillis + 30000; return _start.Task; }
        public Task SubmitAsync(JsonValue move, CancellationToken ct = default) => _r.Run("turns", () =>
        {
            _r.Check(_generation);
            if (Frozen || GameId.Length == 0 || _resync != null || _finish != null)


            {
                throw new KitException("turns", "invalid_state");
            }

            var by = _r.Identity.UserId!; if (_players[Index % _players.Length] != by)
            {
                throw new KitException("turns", "not_your_turn");
            }

            var state = _apply(State, move, by); var record = Record(Index, move, by, _hash); if (Encoding.UTF8.GetByteCount(KitJson.Canonicalize(record)) > 7000)
            {
                throw new KitException("turns", "payload_too_large");
            }

            _room.SendInternal("kit.turn", record); Commit(record, state); return Task.CompletedTask;
        }, ct);
        private JsonValue Record(int index, JsonValue move, string by, string previous) => Json.Object().Set("gameId", GameId).Set("index", index).Set("move", move).Set("by", by).Set("hash", KitJson.Hash(JsonValue.ArrayOf(JsonValue.Of(previous), JsonValue.Of(index), JsonValue.Of(by), move))).Build();
        private void Commit(JsonValue record, JsonValue state)
        {
            if (Encoding.UTF8.GetByteCount(KitJson.Canonicalize(JsonValue.Array(_records.Concat(new[] { record })))) > _max) { Freeze(); return; }
            var by = record.GetString("by")!; _elapsed[by] += _r.Clock.NowMillis - _turnAt; _turnAt = _r.Clock.NowMillis; _records.Add(record); _hash = record.GetString("hash")!; State = state; _persistPending |= _persist; if (Turn != null) { var observed = state; _r.Actions.Enqueue(() => Turn?.Invoke(observed)); }
        }


        private void Freeze() { if (Frozen) { return; } Frozen = true; _resync?.TrySetException(new KitException("turns", "mismatch")); _resync = null; Mismatch?.Invoke(); }
        private bool Validate(JsonValue record, int index, string hash, out JsonValue state, JsonValue? prior = null)
        {
            state = prior ?? State; var by = record.GetString("by"); if (record.GetString("gameId") != GameId || record.GetNumber("index") != index || by != _players[index % _players.Length] || !record.TryGetMember("move", out var move))
            {
                return false;


            }

            if (Encoding.UTF8.GetByteCount(KitJson.Canonicalize(record)) > 7000)
            {
                return false;
            }

            var expected = Record(index, move, by!, hash).GetString("hash"); if (record.GetString("hash") != expected)
            {
                return false;
            }

            try { state = _apply(state, move, by!); return true; } catch (Exception) { return false; }
        }
        public Task ResyncAsync(CancellationToken ct = default) => _r.Run("turns", async () =>
        {
            _r.Check(_generation);
            if (Frozen)
            {
                throw new KitException("turns", "mismatch");
            }

            if (_resync != null) { await _resync.Task; return; }
            _request = Auth.AuthClient.NewNonce(); _responder = _room.Host == _r.Identity.UserId ? Fallback() : _room.Host; _fallback = false; _fallbackAt = _r.Clock.NowMillis + 1000; _manifest = null; _parts.Clear(); _buffer.Clear(); _bytes = 0; _deadline = _r.Clock.NowMillis + 30000; _resync = new TaskCompletionSource<bool>();


            if (_responder == null)
            {
                _resync = null;
                throw new KitException("turns", "unavailable");
            }

            _room.SendInternal("kit.turns.log", Json.Object().Set("gameId", GameId).Set("requestId", _request).Build(), _responder); await _resync.Task;
        }, ct);
        internal void Receive(string name, string from, JsonValue data)
        { try { ReceiveCore(name, from, data); } catch (ArgumentException) { Freeze(); } catch (InvalidOperationException) { Freeze(); } }
        private void ReceiveCore(string name, string from, JsonValue data)
        {
            if (Frozen)
            {
                return;
            }



            if (name == "kit.turns.start")
            {


                if (from != _room.Host && !(from == _responder && data.GetString("requestId") == _request))
                {
                    return;
                }

                if (data.GetString("initialHash") != KitJson.Hash(_initial) || !data.GetArrayOrEmpty("players").Select(x => x.AsString()).SequenceEqual(_players)) { Freeze(); return; }
                var id = data.GetString("gameId"); if (string.IsNullOrEmpty(id) || GameId.Length != 0 && GameId != id) { Freeze(); return; }
                GameId = id!; _start?.TrySetResult(true); _start = null; return;
            }
            if (name == "kit.turns.log") { Reply(from, data); return; }
            if (data.GetString("gameId") != GameId)
            {
                return;
            }

            if (name == "kit.turn")
            {
                if (data.GetString("by") != from) { Freeze(); return; }
                if (_resync != null) { if (_buffer.Count >= 1024 || Encoding.UTF8.GetByteCount(KitJson.Canonicalize(data)) > 7000 || _bytes + Encoding.UTF8.GetByteCount(KitJson.Canonicalize(data)) > _max) { Freeze(); return; } _bytes += Encoding.UTF8.GetByteCount(KitJson.Canonicalize(data)); _buffer.Add(data); return; }
                if (data.GetNumber("index") < Index)
                {
                    return;
                }

                if (!Validate(data, Index, _hash, out var state)) { Freeze(); return; }
                Commit(data, state); return;
            }
            if (name == "kit.turns.finish")
            {
                if (from != _room.Host && !(from == _responder && data.GetString("requestId") == _request))
                {
                    return;
                }

                _finish = data; if (_resync == null)
                {
                    VerifyFinish();
                }

                return;
            }
            if (_resync == null || data.GetString("requestId") != _request)
            {
                return;
            }

            if (name == "kit.turns.manifest")
            {
                if (_responder != from)
                {
                    return;
                }

                var count = data.GetNumber("count"); var chunks = data.GetNumber("chunks"); if (count == null || count < 0 || count > 100000 || count != Math.Floor(count.Value) || chunks == null || chunks < 0 || chunks > _max / 80 || chunks != Math.Floor(chunks.Value) || data.GetString("finalHash") == null) { Freeze(); return; }
                _responder = from; _manifest = data; Assemble();
            }
            else if (name == "kit.turns.chunk" && from == _responder)
            {
                var part = data.GetNumber("part"); if (part == null || part < 0 || part >= _max / 80 || part != Math.Floor(part.Value) || !data.TryGetMember("records", out var records) || records.Kind != JsonKind.Array) { Freeze(); return; }


                if (_parts.TryGetValue((int)part.Value, out var old)) { if (!old.Equals(records)) { Freeze(); } return; }
                _bytes += Encoding.UTF8.GetByteCount(KitJson.Canonicalize(records)); if (_bytes > _max) { Freeze(); return; }
                _parts[(int)part.Value] = records; Assemble();
            }
        }
        private void Reply(string to, JsonValue request)
        {
            if (!_room.Members.Any(x => x.UserId == to) || _outgoing.Count > 0 || Frozen)
            {
                return;
            }

            if (_resync != null || request.GetString("gameId") is not string requested || requested.Length > 0 && requested != GameId)
            {
                return;


            }

            var id = request.GetString("requestId");
            if (id == null || id.Length > 64 || id.Any(c => !char.IsLetterOrDigit(c) && c != '-' && c != '_'))
            {
                return;
            }

            if (string.IsNullOrEmpty(id))
            {
                return;
            }

            var chunks = new List<JsonValue>(); var current = new List<JsonValue>();
            foreach (var record in _records)

            { if (current.Count > 0 && Encoding.UTF8.GetByteCount(KitJson.Canonicalize(JsonValue.Array(current.Concat(new[] { record })))) > 7200) { chunks.Add(JsonValue.Array(current)); current = new List<JsonValue>(); } current.Add(record); }
            if (current.Count > 0)
            {
                chunks.Add(JsonValue.Array(current));
            }

            if (requested.Length == 0)
            {
                _outgoing.Enqueue(("kit.turns.start", Json.Object().Set("gameId", GameId).Set("players", JsonValue.Array(_players.Select(JsonValue.Of))).Set("initialHash", KitJson.Hash(_initial)).Set("requestId", id).Build(), to));
            }

            _outgoing.Enqueue(("kit.turns.manifest", Json.Object().Set("gameId", GameId).Set("requestId", id).Set("count", Index).Set("finalHash", _hash).Set("chunks", chunks.Count).Build(), to));
            for (var i = 0; i < chunks.Count; i++)
            {
                _outgoing.Enqueue(("kit.turns.chunk", Json.Object().Set("gameId", GameId).Set("requestId", id).Set("part", i).Set("records", chunks[i]).Build(), to));
            }

            if (_finish != null) { var b = Json.Object(); foreach (var m in _finish.AsObject()) { b.Set(m.Key, m.Value); } b.Set("requestId", id); _outgoing.Enqueue(("kit.turns.finish", b.Build(), to)); }
        }
        private void Assemble()
        {
            if (_manifest == null)
            {
                return;


            }

            var chunks = (int)_manifest.GetNumber("chunks")!; if (_parts.Count != chunks)
            {
                return;
            }

            var records = new List<JsonValue>(); for (var i = 0; i < chunks; i++) { if (!_parts.TryGetValue(i, out var p)) { return; } records.AddRange(p.AsArray()); }
            if (records.Count < _records.Count || _records.Where((record, i) => i >= records.Count || !record.Equals(records[i])).Any()) { Freeze(); return; }
            if (records.Count != _manifest.GetNumber("count")) { Freeze(); return; }
            var staged = _initial; var hash = Zero;
            for (var i = 0; i < records.Count; i++)
            {
                if (!Validate(records[i], i, hash, out var state, staged)) { Freeze(); return; }
                staged = state; hash = records[i].GetString("hash")!;
            }
            if (hash != _manifest.GetString("finalHash")) { Freeze(); return; }
            foreach (var turn in _buffer)
            {
                var index = turn.GetNumber("index");
                if (index < records.Count) { if (index == null || index < 0 || index != Math.Floor(index.Value) || !records[(int)index.Value].Equals(turn)) { Freeze(); return; } continue; }
                if (!Validate(turn, records.Count, hash, out var state, staged)) { Freeze(); return; }
                records.Add(turn); staged = state; hash = turn.GetString("hash")!;
            }
            if (_finish != null && (_finish.GetNumber("count") != records.Count || _finish.GetString("finalHash") != hash)) { Freeze(); return; }
            if (Encoding.UTF8.GetByteCount(KitJson.Canonicalize(JsonValue.Array(records))) > _max) { Freeze(); return; }
            _records.Clear(); _records.AddRange(records); State = staged; _hash = hash; _buffer.Clear();
            var source = _resync; _resync = null; _persistPending |= _persist; VerifyFinish(); source?.TrySetResult(true);
        }
        public Task FinishAsync(JsonValue result, CancellationToken ct = default) => _r.Run("turns", async () =>
        {


            if (_resync != null)
            {
                await _resync.Task;
            }

            _r.Check(_generation);
            if (Frozen)
            {
                throw new KitException("turns", "mismatch");
            }

            if (!_room.IsHost)
            {
                throw new KitException("turns", "forbidden");
            }

            _finish = Json.Object().Set("gameId", GameId).Set("result", result).Set("count", Index).Set("finalHash", _hash).Build(); _room.SendInternal("kit.turns.finish", _finish); VerifyFinish(); if (_persist)
            {
                await Persist(ct);
            }
        }, ct);
        private void VerifyFinish()
        {


            if (_finish == null || _resync != null)
            {
                return;
            }

            if (_finish.GetNumber("count") != Index || _finish.GetString("finalHash") != _hash) { Freeze(); return; }
            _verifiedFinish = _finish; _persistPending = true;
        }
        private string? Fallback() => _players.FirstOrDefault(id => id != _room.Host && id != _r.Identity.UserId && _room.Members.Any(x => x.UserId == id && x.Online));
        private List<JsonValue> Chunks(IReadOnlyList<JsonValue> records, int limit)
        {
            var chunks = new List<JsonValue>(); var current = new List<JsonValue>();


            foreach (var record in records) { if (current.Count > 0 && Encoding.UTF8.GetByteCount(KitJson.Canonicalize(JsonValue.Array(current.Concat(new[] { record })))) > limit) { chunks.Add(JsonValue.Array(current)); current.Clear(); } current.Add(record); }
            if (current.Count > 0)
            {
                chunks.Add(JsonValue.Array(current));
            }

            return chunks;
        }
        internal async Task Restore(CancellationToken ct)
        {
            if (!_persist)
            {
                return;


            }

            var manifest = await _r.Save.LoadAsync("turns:" + _room.Id, ct); if (manifest == null)
            {
                return;
            }

            if (manifest.GetNumber("v") != 1 || manifest.GetString("initialHash") != KitJson.Hash(_initial) || !manifest.GetArrayOrEmpty("players").Select(x => x.AsString()).SequenceEqual(_players))
            {
                throw new KitException("turns", "mismatch");
            }

            var count = manifest.GetNumber("count"); var chunks = manifest.GetNumber("chunks"); var id = manifest.GetString("gameId");





            var revision = manifest.GetString("revision");
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(revision) || count == null || count < 0 || count > _max / 80 || count != Math.Floor(count.Value) || chunks == null || chunks < 0 || chunks > count || chunks != Math.Floor(chunks.Value))
            {
                throw new KitException("turns", "mismatch");
            }

            var staged = new List<JsonValue>(); var bytes = 0;
            for (var i = 0; i < chunks; i++) { var part = await _r.Save.LoadAsync("turns:" + _room.Id + ":" + id + ":" + revision + ":" + i, ct); if (part == null) { throw new KitException("turns", "unavailable"); } if (part.Kind != JsonKind.Array || part.AsArray().Count == 0) { throw new KitException("turns", "mismatch"); } bytes += Encoding.UTF8.GetByteCount(KitJson.Canonicalize(part)); if (bytes > _max) { throw new KitException("turns", "payload_too_large"); } staged.AddRange(part.AsArray()); }
            var original = GameId; GameId = id!; var oldState = State; State = _initial; var hash = Zero;
            for (var i = 0; i < staged.Count; i++) { if (!Validate(staged[i], i, hash, out var state)) { GameId = original; State = oldState; throw new KitException("turns", "mismatch"); } State = state; hash = staged[i].GetString("hash")!; }
            if (staged.Count != count || hash != manifest.GetString("finalHash")) { GameId = original; State = oldState; throw new KitException("turns", "mismatch"); }
            _records.Clear(); _records.AddRange(staged); _hash = hash;
        }
        private async Task Persist(CancellationToken ct)
        {
            if (Frozen || GameId.Length == 0)
            {
                throw new KitException("turns", "invalid_state");
            }

            var generation = _r.Generation; var records = _records.ToArray(); var hash = _hash; var revision = Auth.AuthClient.NewNonce().Substring(0, 16); var chunks = Chunks(records, 15000); var roomId = _room.Id; var finish = _resync == null ? _verifiedFinish : null;
            if (_persist)
            {
                for (var i = 0; i < chunks.Count; i++) { _r.Check(generation); if (Frozen) { throw new KitException("turns", "mismatch"); } await _r.Save.StoreAsync("turns:" + roomId + ":" + GameId + ":" + revision + ":" + i, chunks[i], new KitSaveOptions { Ttl = _ttl }, ct); }
                _r.Check(generation); if (Frozen)
                {
                    throw new KitException("turns", "mismatch");
                }

                var manifest = Json.Object().Set("v", 1).Set("gameId", GameId).Set("players", JsonValue.Array(_players.Select(JsonValue.Of))).Set("initialHash", KitJson.Hash(_initial)).Set("revision", revision).Set("chunks", chunks.Count).Set("count", records.Length).Set("finalHash", hash).Build();
                await _r.Save.StoreAsync("turns:" + roomId, manifest, new KitSaveOptions { Ttl = _ttl }, ct);
            }
            if (finish != null && finish.TryGetMember("result", out var result)) { _r.Check(generation); await _r.Save.StoreAsync("turns-result:" + GameId, result, new KitSaveOptions { Ttl = _ttl }, ct); }
        }
        internal void Resume() { _retryOnResume = false; }
        internal void Stop() { Frozen = true; _resync?.TrySetCanceled(); _resync = null; _start?.TrySetCanceled(); _start = null; _outgoing.Clear(); _buffer.Clear(); _parts.Clear(); }
        internal void Tick()
        {
            if (Frozen)
            {
                return;
            }

            if (_resync != null && _manifest == null && !_fallback && _r.Clock.NowMillis >= _fallbackAt) { _fallback = true; var peer = Fallback(); if (peer != null) { _responder = peer; _room.SendInternal("kit.turns.log", Json.Object().Set("gameId", GameId).Set("requestId", _request).Build(), peer); } }
            if (_start != null && _r.Clock.NowMillis >= _deadline) { _start.TrySetException(new KitException("turns", "unavailable")); _start = null; }
            if (_resync != null && _r.Clock.NowMillis >= _deadline) { _resync.TrySetException(new KitException("turns", "unavailable")); _resync = null; _parts.Clear(); _buffer.Clear(); }
            if (_outgoing.Count > 0 && _r.Clock.NowMillis >= _sendAt) { var item = _outgoing.Dequeue(); _room.SendInternal(item.Name, item.Data, item.To); _sendAt = _r.Clock.NowMillis + 200; }
            if (_persistPending && !_persistBusy && _r.Foreground && _resync == null && !_retryOnResume) { _persistPending = false; _persistBusy = true; _ = _r.Run("turns", async () => { try { await Persist(CancellationToken.None); } catch (Exception error) { _persistPending = true; _retryOnResume = true; PersistenceFailed?.Invoke(KitRuntime.Map("turns", error)); throw; } finally { _persistBusy = false; } }); }
        }
    }
}
