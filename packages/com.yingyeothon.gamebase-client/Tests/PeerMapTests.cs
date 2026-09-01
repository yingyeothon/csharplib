using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

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

        [Test]
        public void APosThatOmitsDirKeepsThePreviousFacing()
        {
            var map = Create();
            Apply(map, Frames.Snapshot("town", Frames.Peer("bob", 1, 1, "left")));

            Apply(map, Frames.Pos("town", Frames.Peer("bob", 2, 2)));

            Assert.That(map.Get("bob")!.Dir, Is.EqualTo("left"));

            Apply(map, Frames.Pos("town", Frames.Peer("bob", 3, 3, "right")));

            Assert.That(map.Get("bob")!.Dir, Is.EqualTo("right"));
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
    }
}
