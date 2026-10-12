using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Yingyeothon.Codec;
using Yingyeothon.KvStore;

namespace Yingyeothon.GameKit
{
    /// <summary>Published content cache with foreground polling.</summary>
    public interface IKitContent
    {
        /// <summary>Cached read with background refresh, or an awaited cold read.</summary>
        Task<JsonValue?> GetAsync(string key, CancellationToken cancellationToken = default);
        /// <summary>Lists all pages of published entries.</summary>
        Task<IReadOnlyList<KvListEntry>> ListAsync(string? prefix = null, CancellationToken cancellationToken = default);
        /// <summary>Observes version changes; completion and handlers run inside Poll.</summary>
        IDisposable Subscribe(string key, Action<JsonValue?> changed);
    }
    /// <summary>Conditional save options.</summary>
    public sealed class KitSaveOptions
    {
        /// <summary>Local/remote merge for this request; registered policies are used after restoration.</summary>
        public Func<JsonValue?, JsonValue?, JsonValue>? Merge { get; set; }
        /// <summary>Optional TTL in seconds.</summary>
        public int? Ttl { get; set; }
    }
    /// <summary>Player save operations; counters and claims are never queued offline.</summary>
    public interface IKitSave
    {
        /// <summary>Loads and remembers the conditional-write version.</summary>
        Task<JsonValue?> LoadAsync(string name, CancellationToken cancellationToken = default);
        /// <summary>Conditionally stores, merging on conflict only when requested.</summary>
        Task StoreAsync(string name, JsonValue value, KitSaveOptions? options = null, CancellationToken cancellationToken = default);
        /// <summary>Loads, applies a local function and conditionally stores.</summary>
        Task UpdateAsync(string name, Func<JsonValue?, JsonValue> update, KitSaveOptions? options = null, CancellationToken cancellationToken = default);
        /// <summary>Conditionally removes a known revision.</summary>
        Task RemoveAsync(string name, CancellationToken cancellationToken = default);
        /// <summary>Server-atomic integer increment with per-call bounds.</summary>
        Task<long> AddAsync(string name, long delta, KvIncrOptions? options = null, CancellationToken cancellationToken = default);
        /// <summary>Claims once per server TTL window.</summary>
        Task<bool> ClaimOnceAsync(string name, int ttl, CancellationToken cancellationToken = default);
        /// <summary>Replays durable writes in order, stopping at unresolved conflict.</summary>
        Task FlushAsync(CancellationToken cancellationToken = default);
        /// <summary>Loaded queue size.</summary>
        int PendingCount { get; }
    }
    internal sealed class ContentImpl : IKitContent
    {
        private readonly KitRuntime _r;
        private readonly Dictionary<string, KvEntry?> _cache = new Dictionary<string, KvEntry?>();
        private readonly Dictionary<string, List<Action<JsonValue?>>> _watch = new Dictionary<string, List<Action<JsonValue?>>>();
        private double _next; private bool _busy;
        internal ContentImpl(KitRuntime r) { _r = r; }
        public Task<JsonValue?> GetAsync(string key, CancellationToken ct = default) => _r.Run("content", async () =>
        { if (_cache.TryGetValue(key, out var cached)) { _ = _r.Run("content", () => Fetch(key, CancellationToken.None)); return cached?.Value; } return await Fetch(key, ct); }, ct);
        private async Task<JsonValue?> Fetch(string key, CancellationToken ct)
        {
            var generation = _r.Generation; var entry = await _r.Namespace("content", false).GetEntryAsync(key, ct); _r.Check(generation);
            var changed = !_cache.TryGetValue(key, out var old) || old?.Version != entry?.Version;
            _cache[key] = entry;
            if (changed && _watch.TryGetValue(key, out var handlers))
            {
                foreach (var h in handlers.ToArray())
                {
                    h(entry?.Value);
                }
            }

            return entry?.Value;
        }
        public Task<IReadOnlyList<KvListEntry>> ListAsync(string? prefix = null, CancellationToken ct = default) => _r.Run<IReadOnlyList<KvListEntry>>("content", async () =>
        {
            var ns = _r.Namespace("content", false); var generation = _r.Generation; var rows = new List<KvListEntry>(); string? cursor = null; var cursors = new HashSet<string>(); var pages = 0;
            do { var page = await ns.ListAsync(new KvListOptions { Prefix = prefix, Values = true, Cursor = cursor }, ct); _r.Check(generation); rows.AddRange(page.Entries); cursor = page.NextCursor; if (++pages > 2048 || rows.Count > 100000 || cursor != null && !cursors.Add(cursor)) { throw new KitException("content", "unavailable"); } if (cursor != null) { await _r.Delay(0, ct); } } while (cursor != null);
            return rows.AsReadOnly();
        }, ct);
        public IDisposable Subscribe(string key, Action<JsonValue?> changed)
        {


            if (!KvRules.IsKey(key))
            {
                throw new ArgumentException("Invalid content key", nameof(key));
            }

            if (changed == null)
            {
                throw new ArgumentNullException(nameof(changed));
            }

            _r.Actions.Enqueue(() => { if (!_watch.TryGetValue(key, out var list)) { _watch[key] = list = new List<Action<JsonValue?>>(); } list.Add(changed); Refresh(); });
            return new Subscription(() => _r.Actions.Enqueue(() => { if (_watch.TryGetValue(key, out var list)) { list.Remove(changed); } }));
        }
        internal void Refresh() => _next = 0;
        internal void Reset()

        { _cache.Clear(); _busy = false; }
        internal void Tick()
        {
            if (_busy || _r.Clock.NowMillis < _next || _watch.Count == 0)
            {
                return;
            }

            _busy = true; _next = _r.Clock.NowMillis + _r.ContentInterval;
            _ = _r.Run("content", async () => { try { foreach (var key in _watch.Keys.ToArray()) { await Fetch(key, CancellationToken.None); } } finally { _busy = false; } });
        }
        private sealed class Subscription : IDisposable { private Action? _close; internal Subscription(Action close) { _close = close; } public void Dispose() { var close = _close; _close = null; close?.Invoke(); } }
    }
    internal sealed class SaveImpl : IKitSave
    {
        private readonly KitRuntime _r;
        private readonly Dictionary<string, KvEntry?> _base = new Dictionary<string, KvEntry?>();
        private List<JsonValue> _queue = new List<JsonValue>();
        private string? _namespace;
        private readonly Dictionary<string, Func<JsonValue?, JsonValue?, JsonValue>> _livePolicies = new Dictionary<string, Func<JsonValue?, JsonValue?, JsonValue>>();
        private readonly SemaphoreSlim _serial = new SemaphoreSlim(1, 1);
        internal SaveImpl(KitRuntime r) { _r = r; }
        public int PendingCount => _queue.Count;
        internal void Reset() { _livePolicies.Clear(); _base.Clear(); _namespace = null; _queue = new List<JsonValue>(); }
        private async Task Ensure(CancellationToken ct)
        {
            var ns = _r.Field("state", "url") + "|" + _r.Field("auth", "channelId") + "|" + _r.Alias("collections", "save") + "|" + _r.Identity.UserId;


            if (_namespace == ns)
            {
                return;
            }

            var generation = _r.Generation;
            var records = _r.QueueStorage == null ? Array.Empty<JsonValue>() : await _r.QueueStorage.LoadAsync(ns, ct);
            _r.Check(generation); _queue = records.ToList(); _namespace = ns; foreach (var record in _queue)
            {
                var key = record.GetString("name"); if (key == null)
                {
                    throw new KitException("save", "unavailable");


                }

                var version = record.GetNumber("expectedVersion"); record.TryGetMember("value", out var saved); if (!_base.ContainsKey(key))
                {
                    _base[key] = version.HasValue ? new KvEntry(saved ?? JsonValue.Null, (long)version.Value, null) : null;
                }
            }
        }
        private async Task<KvEntry?> Read(string key, CancellationToken ct)
        { var generation = _r.Generation; var entry = await _r.Namespace("save", true).GetEntryAsync(key, ct); _r.Check(generation); _base[key] = entry; return entry; }
        public Task<JsonValue?> LoadAsync(string name, CancellationToken ct = default) => _r.Run("save", async () => { await Ensure(ct); var pending = _queue.LastOrDefault(x => x.GetString("name") == name); if (pending != null) { return pending.GetString("kind") == "remove" ? null : pending.TryGetMember("value", out var local) ? local : JsonValue.Null; } return (await Read(name, ct))?.Value; }, ct);
        public Task StoreAsync(string name, JsonValue value, KitSaveOptions? options = null, CancellationToken ct = default) => _r.Run("save", () => Write(name, value, false, options, ct), ct);
        public Task UpdateAsync(string name, Func<JsonValue?, JsonValue> update, KitSaveOptions? options = null, CancellationToken ct = default)
            => _r.Run("save", async () =>
            {
                if (update == null)
                {
                    throw new ArgumentNullException(nameof(update));
                }

                var generation = _r.Generation;
                await _serial.WaitAsync(ct);
                try
                {
                    _r.Check(generation);
                    await Ensure(ct);
                    var pending = _queue.LastOrDefault(x => x.GetString("name") == name);
                    var prior = pending == null ? (await Read(name, ct))?.Value : pending.GetString("kind") == "remove" ? null : pending.TryGetMember("value", out var value) ? value : JsonValue.Null;
                    await Write(name, update(prior), false, options, ct, true);
                }
                finally { _serial.Release(); }
            }, ct);
        public Task RemoveAsync(string name, CancellationToken ct = default) => _r.Run("save", () => Write(name, null, true, null, ct), ct);
        private async Task Write(string name, JsonValue? value, bool remove, KitSaveOptions? options, CancellationToken ct, bool ownsLock = false)
        {
            if (!remove && value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            var startedGeneration = _r.Generation;
            if (!ownsLock)
            {
                await _serial.WaitAsync(ct);
            }

            try
            {
                _r.Check(startedGeneration);
                await Ensure(ct); if (!_base.ContainsKey(name))
                {
                    await Read(name, ct);
                }

                var known = _base[name]; var expected = known?.Version; var generation = _r.Generation;
                if (_queue.Count > 0) { await Enqueue(name, value, remove, expected, options, ct); return; }
                var revisedValue = value; var revisedExpected = expected;
                try { await Conditional(name, value, remove, expected, options?.Ttl, options?.Merge, ct, (v, e) => { revisedValue = v; revisedExpected = e; }); }
                catch (KvStoreException e) when (_r.QueueStorage != null && (e.Code == KvErrorCodes.Network || e.Code == KvErrorCodes.Timeout || e.Status == 503))
                {
                    _r.Check(generation); await Enqueue(name, revisedValue, remove, revisedExpected, options, ct);
                }
            }
            finally
            {
                if (!ownsLock)
                {
                    _serial.Release();
                }
            }
        }
        private async Task Enqueue(string name, JsonValue? value, bool remove, long? expected, KitSaveOptions? options, CancellationToken ct)
        {
            if (_r.QueueStorage == null)
            {
                throw new KitException("save", "unavailable");
            }

            if (_queue.Count >= 256)
            {
                throw new KitException("save", "unavailable");
            }

            var generation = _r.Generation; var id = Auth.AuthClient.NewNonce();
            var b = Json.Object().Set("id", id).Set("name", name).Set("kind", remove ? "remove" : "store").Set("value", value); if (expected != null)
            {
                b.Set("expectedVersion", (double)expected.Value);
            }

            if (options?.Ttl != null)
            {
                b.Set("ttl", options.Ttl.Value);
            }

            var next = _queue.Concat(new[] { b.Build() }).ToList(); await _r.QueueStorage.StoreAsync(_namespace!, next, ct); _r.Check(generation); _queue = next; if (options?.Merge != null)
            {
                _livePolicies[id] = options.Merge;
            }
        }
        private async Task Conditional(string name, JsonValue? value, bool remove, long? expected, int? ttl, Func<JsonValue?, JsonValue?, JsonValue>? merge, CancellationToken ct, Action<JsonValue, long?>? revised = null)
        {
            var ns = _r.Namespace("save", true); var generation = _r.Generation;
            for (var attempt = 0; ; attempt++)
            {
                try
                {


                    if (remove) { if (expected == null) { var current = await ns.GetEntryAsync(name, ct); _r.Check(generation); if (current != null) { throw new KitException("save", "conflict"); } return; } await ns.DeleteAsync(name, new KvDeleteOptions { IfMatch = expected }, ct); _r.Check(generation); _base[name] = null; return; }
                    var result = await ns.PutAsync(name, value!, new KvPutOptions { IfMatch = expected, IfNoneMatch = expected == null, Ttl = ttl }, ct); _r.Check(generation); _base[name] = new KvEntry(value!, result.Version ?? throw new KitException("save", "unavailable"), result.ExpiresAt); return;
                }
                catch (KvStoreException e) when (e.Status == 409 && merge != null && !remove && attempt < 3)
                { var remote = await ns.GetEntryAsync(name, ct); _r.Check(generation); value = merge(value, remote?.Value); expected = remote?.Version; revised?.Invoke(value, expected); }
            }
        }
        public Task<long> AddAsync(string name, long delta, KvIncrOptions? options = null, CancellationToken ct = default) => _r.Run("save", async () => { var generation = _r.Generation; var result = await _r.Namespace("save", true).IncrAsync(name, delta, options, ct); _r.Check(generation); return result.Value; }, ct);
        public Task<bool> ClaimOnceAsync(string name, int ttl, CancellationToken ct = default) => _r.Run("save", async () =>
        { try { await _r.Namespace("save", true).PutAsync(name, JsonValue.Of(true), new KvPutOptions { IfNoneMatch = true, Ttl = ttl }, ct); return true; } catch (KvStoreException e) when (e.Status == 409 && e.HasCurrentVersion) { return false; } }, ct);
        public Task FlushAsync(CancellationToken ct = default) => _r.Run("save", async () =>
        {
            var startedGeneration = _r.Generation;
            await _serial.WaitAsync(ct); try
            {
                _r.Check(startedGeneration);
                await Ensure(ct); while (_queue.Count > 0)
                {
                    var generation = _r.Generation; var record = _queue[0]; var name = record.GetString("name") ?? throw new KitException("save", "unavailable"); var remove = record.GetString("kind") == "remove"; record.TryGetMember("value", out var value);
                    var expected = record.GetNumber("expectedVersion"); var ttl = record.GetNumber("ttl");
                    try { await Conditional(name, value, remove, expected.HasValue ? (long?)expected.Value : null, ttl.HasValue ? (int?)ttl.Value : null, null, ct); }
                    catch (KvStoreException e) when (e.Status == 409)
                    {
                        var remote = await Read(name, ct); _r.Check(generation);
                        if (!remove && remote?.Value.Equals(value) == true) { }
                        else if (!remove && (_livePolicies.TryGetValue(record.GetString("id") ?? "", out var merge) || _r.Policies.TryGetValue(name, out merge)))


                        {
                            var merged = merge(value, remote?.Value); var b = Json.Object(); foreach (var m in record.AsObject())
                            {
                                if (m.Key != "value" && m.Key != "expectedVersion")
                                {
                                    b.Set(m.Key, m.Value);
                                }
                            }

                            b.Set("value", merged); if (remote != null)
                            {
                                b.Set("expectedVersion", (double)remote.Version);
                            }

                            var revised = _queue.ToList(); revised[0] = b.Build(); await _r.QueueStorage!.StoreAsync(_namespace!, revised, ct); _r.Check(generation); _queue = revised; continue;
                        }
                        else
                        {
                            throw;
                        }
                    }
                    _r.Check(generation); var next = _queue.Skip(1).ToList(); var following = next.FindIndex(x => x.GetString("name") == name); if (following >= 0) { var f = next[following]; var b = Json.Object(); foreach (var m in f.AsObject()) { if (m.Key != "expectedVersion") { b.Set(m.Key, m.Value); } } if (_base.TryGetValue(name, out var current) && current != null) { b.Set("expectedVersion", (double)current.Version); } next[following] = b.Build(); }
                    await _r.QueueStorage!.StoreAsync(_namespace!, next, ct); _r.Check(generation); _queue = next;
                }
            }
            finally { _serial.Release(); }
        }, ct);
    }
}
