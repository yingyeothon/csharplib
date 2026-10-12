using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Yingyeothon.Codec;
using Yingyeothon.KvStore;

namespace Yingyeothon.GameKit.Tests
{
    [TestFixture]
    public class FailureTests
    {
        [TestCase(0d)]
        [TestCase(999d)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public void InvalidIntervalsFailBeforeStarting(double interval)
            => Assert.That(() => GameKit.Create(new GameKitOptions { Config = Json.Object().Build(), ContentPollMillis = interval }), Throws.ArgumentException);
        [TestCase("bad")]
        [TestCase("fixture.invalid.signature")]
        [TestCase("fixture.e30.signature")]
        public void InvalidTokensRemainArgumentErrors(string token)
        {
            using var kit = GameKit.Create(new GameKitOptions { Config = Json.Object().Build() }); var t = kit.Session.FromTokenAsync(token); for (var i = 0; i < 10 && !t.IsCompleted; i++)
            {
                kit.Poll();
            }

            Assert.That(t.Exception!.InnerException, Is.TypeOf<ArgumentException>());
        }
        [TestCase(403, "forbidden")]
        [TestCase(410, "channel_inactive")]
        [TestCase(429, "rate_limited")]
        [TestCase(503, "unavailable")]
        public void ModuleErrorsHaveSharedCodes(int status, string code)
        {
            var store = new KitTests.Store(); var config = Json.Object().Set("state", Json.Object().Set("url", "https://example.test").Build()).Set("auth", Json.Object().Set("url", "https://auth.example.test").Set("channelId", "auth_fixture").Build()).Set("collections", Json.Object().Set("save", "save").Build()).Build();
            using var kit = GameKit.Create(new GameKitOptions { Config = config, Transport = store }); RoomTurnsTests.Pump(kit.Session.FromTokenAsync(RoomTurnsTests.Token("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")), kit); store.ForceStatus = status;
            var t = kit.Save.LoadAsync("k"); for (var i = 0; i < 10 && !t.IsCompleted; i++)
            {
                kit.Poll();
            }

            Assert.That(((KitException)t.Exception!.InnerException!).Code, Is.EqualTo(code));
        }
        [Test]
        public void MissingModulesAndLoginAdapterFailExplicitly()
        {
            using var kit = GameKit.Create(new GameKitOptions { Config = Json.Object().Build() }); var t = kit.Session.SignInAsync(); for (var i = 0; i < 10 && !t.IsCompleted; i++)
            {
                kit.Poll();
            }

            Assert.That(((KitException)t.Exception!.InnerException!).Code, Is.EqualTo("not_configured"));
            Assert.That(() => kit.Board("absent"), Throws.TypeOf<KitException>()); t = kit.Session.RestoreAsync(); RoomTurnsTests.Pump(t, kit);
            t = kit.Matchmaking.FindAsync(); for (var i = 0; i < 10 && !t.IsCompleted; i++)
            {
                kit.Poll();
            }

            Assert.That(((KitException)t.Exception!.InnerException!).Code, Is.EqualTo("lobby_required"));
            kit.Dispose(); kit.Poll(); Assert.That(() => kit.Save.LoadAsync("k"), Throws.InstanceOf<ObjectDisposedException>());
        }
        [Test]
        public void JsonSortsKeysRejectsLoneSurrogatesAndUsesValidPairs()
        {
            Assert.That(KitJson.Canonicalize(Json.Object().Set("z", 1).Set("a", JsonValue.ArrayOf(JsonValue.Null, JsonValue.Of(true), JsonValue.Of(false), JsonValue.Of("😀"))).Build()), Is.EqualTo("{\"a\":[null,true,false,\"😀\"],\"z\":1}"));
            Assert.That(() => KitJson.Canonicalize(JsonValue.Of(new string('\ud800', 1))), Throws.ArgumentException); Assert.That(() => KitJson.Canonicalize(JsonValue.Of(new string('\udc00', 1))), Throws.ArgumentException);
        }
        [Test]
        public void PersistedRevisionUsesSharedChunksAndRestoresTheFullState()
        {
            var gateway = new RoomTurnsTests.Gateway(); var clock = new RoomTurnsTests.Clock(); var store = new KitTests.Store(); using var a = RoomTurnsTests.Kit(gateway, clock, store); RoomTurnsTests.Pump(a.Session.FromTokenAsync(RoomTurnsTests.Token("alice")), a); RoomTurnsTests.Pump(a.Room.CreateAsync(), a);
            var options = RoomTurnsTests.Turns(); options.Persist = true; var game = a.Turns.StartAsync(options); RoomTurnsTests.Pump(game, a); RoomTurnsTests.Pump(game.Result.SubmitAsync(JsonValue.Of(4)), a); for (var i = 0; i < 30; i++)
            {
                a.Poll();
            }

            var manifest = store.Rows["/kv/save/u/me/entries/turns:party_fixture"].Value; Assert.That(manifest.GetNumber("chunks"), Is.EqualTo(1)); Assert.That(manifest.GetNumber("count"), Is.EqualTo(1));
            var restored = a.Turns.StartAsync(options); RoomTurnsTests.Pump(restored, a); Assert.That(restored.Result.GameId, Is.EqualTo(game.Result.GameId)); Assert.That(restored.Result.State.AsNumber(), Is.EqualTo(4)); Assert.That(restored.Result.Index, Is.EqualTo(1));
            RoomTurnsTests.Pump(restored.Result.FinishAsync(JsonValue.Of("win")), a);
        }

        [TestCase(0x7fefffffffffffffL, "1.7976931348623157e+308")]
        [TestCase(0x4430000000000000L, "295147905179352830000")]
        [TestCase(0x0000000000000001L, "5e-324")]
        [TestCase(0x3eb0c6f7a0b5ed8dL, "0.000001")]
        public void CanonicalNumbersCoverFullFiniteIeeeRange(long bits, string expected)
        {
            var value = BitConverter.Int64BitsToDouble(bits);
            Assert.That(KitJson.Canonicalize(JsonValue.Of(value)), Is.EqualTo(expected));
        }
    }
}
