using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Yingyeothon.Logger;

namespace Yingyeothon.Gamebase.Client.Tests
{
    [TestFixture]
    public class PeerMapTests
    {
        private static IPeerMap Create() => PeerMap.Create(new PeerMapOptions { SelfUserId = "alice" });

        private static PeerChange? Apply(IPeerMap map, Yingyeothon.Codec.JsonValue frame)
            => map.Apply(Frames.Read(frame));

        [Test]
        public void ASnapshotReplacesEverythingAndDropsSelf()
        {
            var map = Create();

            var change = Apply(map, Frames.Snapshot("town", Frames.Peer("bob", 1, 2), Frames.Peer("alice", 9, 9)));

            Assert.That(change!.Kind, Is.EqualTo(PeerChangeKind.Snapshot));
            Assert.That(change.Zone, Is.EqualTo("town"));
            Assert.That(map.Zone, Is.EqualTo("town"));
            Assert.That(map.All().Select(p => p.UserId), Is.EqualTo(new[] { "bob" }));
            Assert.That(map.Get("alice"), Is.Null);

            Apply(map, Frames.Snapshot("cave", Frames.Peer("carol", 3, 4)));

            Assert.That(map.Zone, Is.EqualTo("cave"));
            Assert.That(map.All().Select(p => p.UserId), Is.EqualTo(new[] { "carol" }));
        }

        [Test]
        public void FramesBeforeTheFirstSnapshotAreIgnored()
        {
            var map = Create();

            Assert.That(Apply(map, Frames.Enter("town", "bob", 1, 1)), Is.Null);
            Assert.That(Apply(map, Frames.Pos("town", Frames.Peer("bob", 2, 2))), Is.Null);
            Assert.That(Apply(map, Frames.Leave("town", "bob")), Is.Null);
            Assert.That(map.All(), Is.Empty);
        }

        [Test]
        public void EnterAddsAPeerAndLeaveRemovesIt()
        {
            var map = Create();
            Apply(map, Frames.Snapshot("town"));

            var entered = Apply(map, Frames.Enter("town", "bob", 1, 2, "n"));

            Assert.That(entered!.Kind, Is.EqualTo(PeerChangeKind.Enter));
            Assert.That(entered.Peers.Single().Dir, Is.EqualTo("n"));
            Assert.That(map.Get("bob")!.X, Is.EqualTo(1d));

            var left = Apply(map, Frames.Leave("town", "bob"));

            Assert.That(left!.Kind, Is.EqualTo(PeerChangeKind.Leave));
            Assert.That(left.UserId, Is.EqualTo("bob"));
            Assert.That(map.Get("bob"), Is.Null);
        }

        [Test]
        public void LeavingTwiceIsANoOpTheSecondTime()
        {
            var map = Create();
            Apply(map, Frames.Snapshot("town", Frames.Peer("bob", 1, 1)));

            Assert.That(Apply(map, Frames.Leave("town", "bob")), Is.Not.Null);
            Assert.That(Apply(map, Frames.Leave("town", "bob")), Is.Null);
        }

        [Test]
        public void ALatePosCannotResurrectAPeerThatLeft()
        {
            // The gateway coalesces positions per tick, so a pos batch can describe a
            // peer that has already left. Re-adding it would leave a permanent ghost.
            var map = Create();
            Apply(map, Frames.Snapshot("town", Frames.Peer("bob", 1, 1)));
            Apply(map, Frames.Leave("town", "bob"));

            var change = Apply(map, Frames.Pos("town", Frames.Peer("bob", 5, 5)));

            Assert.That(change, Is.Null);
            Assert.That(map.Get("bob"), Is.Null);
        }

        [Test]
        public void PosUpdatesKnownPeersAndFiltersSelf()
        {
            var map = Create();
            Apply(map, Frames.Snapshot("town", Frames.Peer("bob", 1, 1), Frames.Peer("carol", 2, 2)));

            var change = Apply(map, Frames.Pos(
                "town",
                Frames.Peer("alice", 9, 9),
                Frames.Peer("bob", 3, 4),
                Frames.Peer("dave", 7, 7)));

            Assert.That(change!.Kind, Is.EqualTo(PeerChangeKind.Move));
            Assert.That(change.Peers.Select(p => p.UserId), Is.EqualTo(new[] { "bob" }));
            Assert.That(map.Get("bob")!.Y, Is.EqualTo(4d));
            Assert.That(map.Get("carol")!.X, Is.EqualTo(2d));
            Assert.That(map.Get("alice"), Is.Null);
            Assert.That(map.Get("dave"), Is.Null);
        }

        [Test]
        public void PosWithNoKnownMoverProducesNoChange()
        {
            var map = Create();
            Apply(map, Frames.Snapshot("town"));

            Assert.That(Apply(map, Frames.Pos("town", Frames.Peer("alice", 1, 1))), Is.Null);
        }

        /// <remarks>
        /// The gateway rebuilds the whole peer from each inbound pos
        /// (<c>Peer{UserID, X, Y, Dir: in.Dir}</c>) and marshals it with
        /// <c>dir,omitempty</c>, so an omitted <c>dir</c> in a batch says the peer has
        /// no facing — it is not "unchanged". Carrying the old value forward left a
        /// peer facing a direction it had cleared, with no later frame able to correct
        /// it, and made the same peer's facing depend on whether it arrived by
        /// <c>snapshot</c> (null) or by <c>pos</c> (stale).
        /// </remarks>
        [Test]
        public void APosThatOmitsDirClearsTheFacingBecauseTheGatewaySaysSo()
        {
            var map = Create();
            Apply(map, Frames.Snapshot("town", Frames.Peer("bob", 1, 1, "left")));

            Apply(map, Frames.Pos("town", Frames.Peer("bob", 2, 2)));

            Assert.That(map.Get("bob")!.Dir, Is.Null);

            Apply(map, Frames.Pos("town", Frames.Peer("bob", 3, 3, "right")));

            Assert.That(map.Get("bob")!.Dir, Is.EqualTo("right"));

            // And the same peer arriving through either frame agrees.
            Apply(map, Frames.Snapshot("town", Frames.Peer("bob", 4, 4)));

            Assert.That(map.Get("bob")!.Dir, Is.Null);
        }

        [Test]
        public void FramesForAnotherZoneAreIgnored()
        {
            var map = Create();
            Apply(map, Frames.Snapshot("town", Frames.Peer("bob", 1, 1)));

            Assert.That(Apply(map, Frames.Enter("cave", "carol", 1, 1)), Is.Null);
            Assert.That(Apply(map, Frames.Pos("cave", Frames.Peer("bob", 5, 5))), Is.Null);
            Assert.That(Apply(map, Frames.Leave("cave", "bob")), Is.Null);
            Assert.That(map.Get("bob")!.X, Is.EqualTo(1d));
        }

        [Test]
        public void EnteringSelfIsIgnored()
        {
            var map = Create();
            Apply(map, Frames.Snapshot("town"));

            Assert.That(Apply(map, Frames.Enter("town", "alice", 1, 1)), Is.Null);
            Assert.That(map.All(), Is.Empty);
        }

        [Test]
        public void ReadsReturnCopiesTheCallerCannotUseToMutateTheMap()
        {
            var map = Create();
            Apply(map, Frames.Snapshot("town", Frames.Peer("bob", 1, 1)));

            var first = map.Get("bob");
            Apply(map, Frames.Pos("town", Frames.Peer("bob", 9, 9)));

            Assert.That(first!.X, Is.EqualTo(1d));
            Assert.That(map.Get("bob")!.X, Is.EqualTo(9d));

            var all = map.All();
            Assert.That(all, Is.Not.SameAs(map.All()));
        }

        [Test]
        public void ResetForgetsTheZoneAndThePeers()
        {
            var map = Create();
            Apply(map, Frames.Snapshot("town", Frames.Peer("bob", 1, 1)));

            map.Reset();

            Assert.That(map.Zone, Is.Null);
            Assert.That(map.All(), Is.Empty);
            Assert.That(Apply(map, Frames.Pos("town", Frames.Peer("bob", 2, 2))), Is.Null);
        }

        [Test]
        public void UnrelatedFramesAreIgnored()
        {
            var map = Create();
            Apply(map, Frames.Snapshot("town"));

            Assert.That(map.Apply(Frames.Read(Frames.Hello())), Is.Null);
        }

        private static IPeerMap CreateLogged(CapturingLogWriter log)
            => PeerMap.Create(new PeerMapOptions
            {
                SelfUserId = "alice",
                Logger = FilteredLogger.Create(new FilteredLoggerOptions { Severity = LogSeverity.Debug, Writer = log }),
            });

        [Test]
        public void ALeaveForAnUnknownPeerIsIgnoredAndLoggedWithTheIdAndZoneOnly()
        {
            var log = new CapturingLogWriter();
            var map = CreateLogged(log);
            Apply(map, Frames.Snapshot("town", Frames.Peer("bob", 1, 2)));

            Assert.That(Apply(map, Frames.Leave("town", "mallory")), Is.Null);

            Assert.That(log.Lines, Has.Count.EqualTo(1));
            Assert.That(log.Lines[0], Does.Contain("leave for an unknown peer"));
            Assert.That(log.Lines[0], Does.Contain("mallory"));
            Assert.That(log.Lines[0], Does.Contain("town"));
            Assert.That(map.All().Select(p => p.UserId), Is.EqualTo(new[] { "bob" }));
        }

        [Test]
        public void APosForAnUnknownPeerIsDroppedAndLoggedWhileKnownPeersStillMove()
        {
            var log = new CapturingLogWriter();
            var map = CreateLogged(log);
            Apply(map, Frames.Snapshot("town", Frames.Peer("bob", 1, 2)));

            var change = Apply(map, Frames.Pos("town", Frames.Peer("bob", 5, 5), Frames.Peer("mallory", 7, 7)));

            Assert.That(change!.Peers.Select(p => p.UserId), Is.EqualTo(new[] { "bob" }));
            Assert.That(map.Get("mallory"), Is.Null);
            Assert.That(log.Lines, Has.Count.EqualTo(1));
            Assert.That(log.Lines[0], Does.Contain("pos for an unknown peer"));
            Assert.That(log.Lines[0], Does.Contain("mallory"));
            // The position is the frame's content, not a routing fact.
            Assert.That(log.Lines[0], Does.Not.Contain("7"));
        }

        [Test]
        public void KnownPeersSelfAndOtherZonesLogNothing()
        {
            var log = new CapturingLogWriter();
            var map = CreateLogged(log);

            // Before any snapshot the map has no zone, so nothing is "unknown" yet.
            Apply(map, Frames.Leave("town", "bob"));
            Apply(map, Frames.Snapshot("town", Frames.Peer("bob", 1, 2)));
            Apply(map, Frames.Pos("town", Frames.Peer("alice", 0, 0), Frames.Peer("bob", 2, 2)));
            Apply(map, Frames.Leave("cave", "carol"));
            Apply(map, Frames.Pos("cave", Frames.Peer("carol", 1, 1)));
            Apply(map, Frames.Leave("town", "bob"));

            Assert.That(log.Lines, Is.Empty);
        }

        [Test]
        public void AnUnknownPeerDiagnosticIsCappedAndStripped()
        {
            var log = new CapturingLogWriter();
            var map = CreateLogged(log);
            Apply(map, Frames.Snapshot("town"));

            Apply(map, Frames.Leave("town", "evil\nFORGED " + new string('x', 200)));

            Assert.That(log.Lines, Has.Count.EqualTo(1));
            Assert.That(log.Lines[0], Does.Contain("evil?FORGED"));
            Assert.That(log.Lines[0], Does.Not.Contain(new string('x', 40)));
        }

        [Test]
        public void EachUnknownPeerIsReportedOncePerZone()
        {
            var log = new CapturingLogWriter();
            var map = CreateLogged(log);
            Apply(map, Frames.Snapshot("town"));

            // A gateway bug repeats every tick; the log must not.
            for (var tick = 0; tick < 5; tick++)
            {
                Apply(map, Frames.Pos("town", Frames.Peer("mallory", tick, tick)));
            }

            Apply(map, Frames.Leave("town", "mallory"));
            Assert.That(log.Lines, Has.Count.EqualTo(1));

            // A new zone is a new view, and a fresh report.
            Apply(map, Frames.Snapshot("cave"));
            Apply(map, Frames.Pos("cave", Frames.Peer("mallory", 1, 1)));
            Assert.That(log.Lines, Has.Count.EqualTo(2));
        }

        [Test]
        public void TheReportsAreCappedWhateverTheGatewaySends()
        {
            var log = new CapturingLogWriter();
            var map = CreateLogged(log);
            Apply(map, Frames.Snapshot("town"));
            var flood = new Yingyeothon.Codec.JsonValue[1000];
            for (var i = 0; i < flood.Length; i++)
            {
                flood[i] = Frames.Peer("ghost" + i, 1, 1);
            }

            Apply(map, Frames.Pos("town", flood));
            Apply(map, Frames.Pos("town", flood));

            // 256 reports, then one line saying the rest are not logged — once.
            Assert.That(log.Lines, Has.Count.EqualTo(257));
            Assert.That(log.Lines[256], Does.Contain("past the report cap"));
        }

        [Test]
        public void AHugeIdIsKeptOnlyInItsBoundedForm()
        {
            var log = new CapturingLogWriter();
            var map = CreateLogged(log);
            Apply(map, Frames.Snapshot("town"));
            var huge = new string('x', 60000);

            // Two ids sharing their first 32 characters are one report: the set holds the
            // diagnostic form, so a hostile id cannot pin a frame's worth of memory.
            Apply(map, Frames.Leave("town", huge + "a"));
            Apply(map, Frames.Leave("town", huge + "b"));

            Assert.That(log.Lines, Has.Count.EqualTo(1));
        }

        [Test]
        public void ALeaveNamingSelfIsIgnoredWithoutAReport()
        {
            var log = new CapturingLogWriter();
            var map = CreateLogged(log);
            Apply(map, Frames.Snapshot("town", Frames.Peer("bob", 1, 1)));

            Assert.That(Apply(map, Frames.Leave("town", "alice")), Is.Null);
            Assert.That(log.Lines, Is.Empty);
        }

        [TestCase("a\u0085b", "a?b")]
        [TestCase("a\u2028b\u2029c", "a?b?c")]
        [TestCase("a\u202Eb\u2066c\u200Bd", "a?b?c?d")]
        [TestCase("\u0000\u001F\u007F", "???")]
        [TestCase("zone-\uAC00", "zone-\uAC00")]
        [TestCase("emoji-\uD83D\uDE00", "emoji-\uD83D\uDE00")]
        public void TheDiagnosticReplacesEveryCharacterThatCanBreakOrReorderALine(string id, string rendered)
        {
            var log = new CapturingLogWriter();
            var map = CreateLogged(log);
            Apply(map, Frames.Snapshot("town"));

            Apply(map, Frames.Leave("town", id));

            Assert.That(log.Lines[0], Does.Contain(Yingyeothon.Codec.Json.Stringify(Yingyeothon.Codec.JsonValue.Of(rendered))));
        }

        [Test]
        public void ALoneSurrogateIsReplacedToo()
        {
            // Built at run time: an attribute argument is stored as UTF-8, which turns a
            // lone surrogate into U+FFFD before the test ever sees it.
            var log = new CapturingLogWriter();
            var map = CreateLogged(log);
            Apply(map, Frames.Snapshot("town"));

            Apply(map, Frames.Leave("town", "a" + (char)0xD800 + "b"));

            Assert.That(log.Lines[0], Does.Contain("\"a?b\""));
        }

        [Test]
        public void APairSplitByTheCutIsReplacedNotHalved()
        {
            var log = new CapturingLogWriter();
            var map = CreateLogged(log);
            Apply(map, Frames.Snapshot("town"));

            // 31 characters, then an emoji whose low half falls past the 32-character cut.
            Apply(map, Frames.Leave("town", new string('x', 31) + "\uD83D\uDE00"));

            Assert.That(log.Lines[0], Does.Contain(new string('x', 31) + "?\u2026"));
        }
    }
}
