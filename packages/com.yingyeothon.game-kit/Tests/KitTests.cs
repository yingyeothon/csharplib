using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Yingyeothon.Codec;
using Yingyeothon.Gamebase.Client;
using Yingyeothon.KvStore;

namespace Yingyeothon.GameKit.Tests
{
    [TestFixture]
    public class KitTests
    {
        internal sealed class Clock : IClock { public double NowMillis { get; set; } }
        internal sealed class Store : IHttpTransport
        {
            internal readonly Dictionary<string, (JsonValue Value, long Version)> Rows = new Dictionary<string, (JsonValue, long)>();
            internal int TimeCalls; internal int ForceStatus; internal bool Offline;
            public Task<HttpReply> SendAsync(HttpCall call, CancellationToken ct)
            {
                if (Offline)
                {
                    throw new InvalidOperationException("offline fixture");
                }

                var path = call.Url.AbsolutePath; var headers = new List<KeyValuePair<string, string>>();
                HttpReply Reply(int status, JsonValue? body = null) => new HttpReply(status, headers, body == null ? "" : Json.Stringify(body));
                if (ForceStatus > 0)
                {
                    return Task.FromResult(Reply(ForceStatus));
                }

                if (path == "/time") { TimeCalls++; return Task.FromResult(Reply(200, Json.Object().Set("epochMs", 1700000000000d).Build())); }
                var key = path.Substring(path.LastIndexOf('/') + 1); var exists = Rows.TryGetValue(path, out var row);
                string? Header(string name) => call.Headers.FirstOrDefault(x => x.Key == name).Value;
                if (call.Method == "GET")
                {
                    if (!exists)
                    {
                        return Task.FromResult(Reply(404));
                    }

                    headers.Add(new KeyValuePair<string, string>("etag", row.Version.ToString())); return Task.FromResult(Reply(200, row.Value));
                }
                var expected = Header("If-Match"); var absent = Header("If-None-Match") != null;
                if (expected != null && (!exists || expected != "\"" + row.Version + "\"") || absent && exists)
                {
                    return Task.FromResult(Reply(409, Json.Object().Set("error", Json.Object().Set("code", "conflict").Set("details", Json.Object().Set("current", exists ? JsonValue.Of((double)row.Version) : JsonValue.Null).Build()).Build()).Build()));
                }

                if (call.Method == "DELETE") { Rows.Remove(path); return Task.FromResult(Reply(exists ? 204 : 404)); }
                var value = Json.Parse(call.Body!);
                if (call.Method == "PATCH")
                {
                    var n = (exists ? row.Value.AsNumber() : 0) + value.GetNumber("incr")!.Value;
                    if (n < value.GetNumber("min") || n > value.GetNumber("max"))
                    {
                        return Task.FromResult(Reply(409, Json.Object().Set("error", Json.Object().Set("code", "conflict").Set("details", Json.Object().Set("reason", "out_of_range").Build()).Build()).Build()));
                    }

                    Rows[path] = (JsonValue.Of(n), exists ? row.Version + 1 : 1); return Task.FromResult(Reply(200, Json.Object().Set("value", n).Set("version", Rows[path].Version).Build()));
                }
                Rows[path] = (value, exists ? row.Version + 1 : 1); headers.Add(new KeyValuePair<string, string>("etag", Rows[path].Version.ToString())); return Task.FromResult(Reply(exists ? 204 : 201));
            }
        }
        private static string Token(string user = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", long expiry = 1700000900)
        { var claims = Json.Object().Set("sub", user).Set("exp", (double)expiry).Build(); return "fixture." + Convert.ToBase64String(Encoding.UTF8.GetBytes(Json.Stringify(claims))).TrimEnd('=').Replace('+', '-').Replace('/', '_') + ".signature"; }
        private static JsonValue Config() => Json.Object().Set("auth", Json.Object().Set("url", "https://auth.example.test").Set("channelId", "auth_fixture").Build()).Set("state", Json.Object().Set("url", "https://state.example.test").Build()).Set("collections", Json.Object().Set("save", "save").Set("content", "content").Set("mail", "mail").Build()).Build();
        private static IGameKit Kit(Store store, Clock? clock = null, IKitQueueStorage? queue = null) => GameKit.Create(new GameKitOptions { Config = Config(), Transport = store, Clock = clock, OfflineQueue = queue });
        private static void Complete(IGameKit kit, Task task)
        { for (var i = 0; i < 1000 && !task.IsCompleted; i++) { kit.Poll(); } Assert.That(task.IsCompleted, Is.True, "pump stalled"); task.GetAwaiter().GetResult(); }
        [Test]
        public void SessionUsesPlatformTimeAndExpiresOnceOnThePump()
        {
            var store = new Store(); var clock = new Clock(); using var kit = Kit(store, clock); var expiring = 0; var outCount = 0; kit.Session.Expiring += () => expiring++; kit.Session.SignedOut += () => outCount++;
            var task = kit.Session.FromTokenAsync(Token()); Assert.That(task.IsCompleted, Is.False); Complete(kit, task); Assert.That(kit.Session.Now(), Is.EqualTo(1700000000000d));
            clock.NowMillis = 300000; kit.Poll(); kit.Poll(); Assert.That(expiring, Is.EqualTo(1)); clock.NowMillis = 900000; kit.Poll(); kit.Poll(); Assert.That(outCount, Is.EqualTo(1)); Assert.That(kit.Session.Token, Is.Null);
        }
        [Test]
        public void SaveConflictNeverOverwritesAndMergeIsExplicit()
        {
            var store = new Store(); using var a = Kit(store); using var b = Kit(store); Complete(a, a.Session.FromTokenAsync(Token())); Complete(b, b.Session.FromTokenAsync(Token()));
            Complete(a, a.Save.LoadAsync("level")); Complete(b, b.Save.LoadAsync("level")); Complete(a, a.Save.StoreAsync("level", JsonValue.Of(1)));
            var conflict = b.Save.StoreAsync("level", JsonValue.Of(2)); for (var i = 0; i < 100 && !conflict.IsCompleted; i++)
            {
                b.Poll();
            }

            Assert.That(((KitException)conflict.Exception!.InnerException!).Code, Is.EqualTo("conflict"));
            Complete(b, b.Save.StoreAsync("level", JsonValue.Of(2), new KitSaveOptions { Merge = (local, remote) => JsonValue.Of(local!.AsNumber() + remote!.AsNumber()) }));
            var saved = a.Save.LoadAsync("level"); Complete(a, saved); Assert.That(saved.Result!.AsNumber(), Is.EqualTo(3));
        }
        [Test]
        public void ClaimsAndCounterBoundsUseServerAtomicOperations()
        {
            var store = new Store(); using var kit = Kit(store); Complete(kit, kit.Session.FromTokenAsync(Token()));
            var first = kit.Save.ClaimOnceAsync("reward", 10); Complete(kit, first); var second = kit.Save.ClaimOnceAsync("reward", 10); Complete(kit, second); Assert.That(first.Result, Is.True); Assert.That(second.Result, Is.False);
            Complete(kit, kit.Save.AddAsync("wallet", 1, new KvIncrOptions { Min = 0 })); var fail = kit.Save.AddAsync("wallet", -2, new KvIncrOptions { Min = 0 }); for (var i = 0; i < 100 && !fail.IsCompleted; i++)
            {
                kit.Poll();
            }

            Assert.That(((KitException)fail.Exception!.InnerException!).Code, Is.EqualTo("out_of_range"));
        }
        [Test]
        public void UnauthorizedStopsEveryModuleOnce()
        {
            var store = new Store(); using var kit = Kit(store); Complete(kit, kit.Session.FromTokenAsync(Token())); var count = 0; kit.Session.SignedOut += () => count++; store.ForceStatus = 401;
            var task = kit.Save.LoadAsync("k"); for (var i = 0; i < 100 && !task.IsCompleted; i++)
            {
                kit.Poll();
            }

            Assert.That(((KitException)task.Exception!.InnerException!).Code, Is.EqualTo("signed_out")); Assert.That(count, Is.EqualTo(1)); Assert.That(kit.Session.Token, Is.Null);
        }
        [TestCase("[\"0000000000000000000000000000000000000000000000000000000000000000\",0,\"alice\",{\"x\":1,\"y\":2}]", "8e48ac7f284692548995d918cbc6328694734bef04eb5dad33144fcd7d300c2a")]
        [TestCase("[\"0000000000000000000000000000000000000000000000000000000000000000\",1,\"alice\",{\"ok\":true,\"text\":\"안녕\"}]", "de6d3a87f5523e1f59808cb6072bafefb66db18938fabcf516fd808549607935")]
        public void HashMatchesTheSharedContract(string json, string expected) => Assert.That(KitJson.Hash(Json.Parse(json)), Is.EqualTo(expected));
        [TestCase(1e-6, "0.000001")]
        [TestCase(1e-7, "1e-7")]
        [TestCase(1e20, "100000000000000000000")]
        [TestCase(1e21, "1e+21")]
        [TestCase(-0d, "0")]
        public void CanonicalNumbersUseEcmaFormatting(double value, string expected) => Assert.That(KitJson.Canonicalize(JsonValue.Of(value)), Is.EqualTo(expected));
        private sealed class Queue : IKitQueueStorage
        {
            internal readonly Dictionary<string, IReadOnlyList<JsonValue>> Records = new Dictionary<string, IReadOnlyList<JsonValue>>();
            public Task<IReadOnlyList<JsonValue>> LoadAsync(string ns, CancellationToken ct) => Task.FromResult(Records.TryGetValue(ns, out var records) ? records : (IReadOnlyList<JsonValue>)Array.Empty<JsonValue>());
            public Task StoreAsync(string ns, IReadOnlyList<JsonValue> records, CancellationToken ct) { Records[ns] = records.ToArray(); return Task.CompletedTask; }
        }
        [Test]
        public void OfflineConditionalIntentIsDurableAndReplayedInOrder()
        {
            var store = new Store(); var queue = new Queue(); using var kit = Kit(store, null, queue); Complete(kit, kit.Session.FromTokenAsync(Token())); Complete(kit, kit.Save.LoadAsync("k")); store.Offline = true;
            Complete(kit, kit.Save.StoreAsync("k", JsonValue.Of(1))); Assert.That(kit.Save.PendingCount, Is.EqualTo(1)); Assert.That(queue.Records.Single().Value.Count, Is.EqualTo(1));
            store.Offline = false; Complete(kit, kit.Save.FlushAsync()); Assert.That(kit.Save.PendingCount, Is.EqualTo(0)); var read = kit.Save.LoadAsync("k"); Complete(kit, read); Assert.That(read.Result!.AsNumber(), Is.EqualTo(1));
        }

        [Test]
        public void ConcurrentUpdatesAreSerializedAndOfflineWritesRebaseTheirOwnVersions()
        {
            var store = new Store(); var queue = new Queue(); using var kit = Kit(store, null, queue); Complete(kit, kit.Session.FromTokenAsync(Token())); Complete(kit, kit.Save.StoreAsync("k", JsonValue.Of(0)));
            var first = kit.Save.UpdateAsync("k", v => JsonValue.Of(v!.AsNumber() + 1)); var second = kit.Save.UpdateAsync("k", v => JsonValue.Of(v!.AsNumber() + 1)); Complete(kit, Task.WhenAll(first, second));
            var read = kit.Save.LoadAsync("k"); Complete(kit, read); Assert.That(read.Result!.AsNumber(), Is.EqualTo(2)); store.Offline = true;
            Complete(kit, kit.Save.StoreAsync("k", JsonValue.Of(3), new KitSaveOptions { Ttl = 30 })); Complete(kit, kit.Save.UpdateAsync("k", v => JsonValue.Of(v!.AsNumber() + 1))); var overlay = kit.Save.LoadAsync("k"); Complete(kit, overlay); Assert.That(overlay.Result!.AsNumber(), Is.EqualTo(4));
            store.Offline = false; Complete(kit, kit.Save.FlushAsync()); read = kit.Save.LoadAsync("k"); Complete(kit, read); Assert.That(read.Result!.AsNumber(), Is.EqualTo(4)); Complete(kit, kit.Save.RemoveAsync("k")); read = kit.Save.LoadAsync("k"); Complete(kit, read); Assert.That(read.Result, Is.Null);
        }
        [TestCase(false)]
        [TestCase(true)]
        public void RestoredQueueKeepsConflictAndMergesOnlyWithARegisteredPolicy(bool merge)
        {
            var store = new Store(); var queue = new Queue(); using var a = Kit(store, null, queue); Complete(a, a.Session.FromTokenAsync(Token())); Complete(a, a.Save.LoadAsync("k")); store.Offline = true; Complete(a, a.Save.StoreAsync("k", JsonValue.Of(1))); store.Offline = false;
            var config = Config(); var options = new GameKitOptions { Config = config, Transport = store, OfflineQueue = queue }; if (merge)
            {
                options.MergePolicies = new Dictionary<string, Func<JsonValue?, JsonValue?, JsonValue>> { { "k", (local, remote) => JsonValue.Of(local!.AsNumber() + remote!.AsNumber()) } };
            }

            using var b = GameKit.Create(options); Complete(b, b.Session.FromTokenAsync(Token())); store.Rows["/kv/save/u/me/entries/k"] = (JsonValue.Of(10), 1);
            var flush = b.Save.FlushAsync(); for (var i = 0; i < 100 && !flush.IsCompleted; i++)
            {
                b.Poll();
            }

            if (merge) { flush.GetAwaiter().GetResult(); Assert.That(b.Save.PendingCount, Is.Zero); Assert.That(store.Rows["/kv/save/u/me/entries/k"].Value.AsNumber(), Is.EqualTo(11)); }
            else { Assert.That(((KitException)flush.Exception!.InnerException!).Code, Is.EqualTo("conflict")); Assert.That(b.Save.PendingCount, Is.EqualTo(1)); }
        }
        private sealed class Tokens : IKitTokenStorage
        {
            internal string? Value; internal int Clears;
            public Task<string?> LoadAsync(CancellationToken ct) => Task.FromResult(Value);
            public Task StoreAsync(string value, CancellationToken ct) { Value = value; return Task.CompletedTask; }
            public Task ClearAsync(CancellationToken ct) { Value = null; Clears++; return Task.CompletedTask; }
        }
        [Test]
        public void StorageRestorationAndExplicitSignoutClearTheToken()
        {
            var store = new Store(); var tokens = new Tokens { Value = Token() }; using var kit = GameKit.Create(new GameKitOptions { Config = Config(), Transport = store, TokenStorage = tokens }); Complete(kit, kit.Session.RestoreAsync()); Assert.That(kit.Session.Token, Is.Not.Null); Complete(kit, kit.Session.SignOutAsync()); Assert.That(tokens.Value, Is.Null); Assert.That(tokens.Clears, Is.EqualTo(1));
        }
        [TestCase("offline", true)]
        [TestCase("conflict", true)]
        [TestCase("unavailable", true)]
        [TestCase("rate_limited", true)]
        [TestCase("inbox_full", true)]
        [TestCase("cooldown", true)]
        [TestCase("signed_out", false)]
        public void RetryabilityIsTheSharedContract(string code, bool retry) => Assert.That(new KitException("fixture", code).Retryable, Is.EqualTo(retry));
    }
}
