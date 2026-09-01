using System;
using System.Collections.Generic;

namespace Yingyeothon.Gamebase.Client
{
    /// <summary>What applying a frame did to the peer map.</summary>
    public enum PeerChangeKind
    {
        Snapshot,
        Enter,
        Leave,
        Move,
    }

    /// <summary>One change produced by <see cref="IPeerMap.Apply"/>.</summary>
    public sealed class PeerChange
    {
        internal PeerChange(PeerChangeKind kind, string? zone, IReadOnlyList<Peer> peers, string? userId)
        {
            Kind = kind;
            Zone = zone;
            Peers = peers;
            UserId = userId;
        }

        public PeerChangeKind Kind { get; }

        /// <summary>Set on a snapshot.</summary>
        public string? Zone { get; }

        /// <summary>The peers a snapshot, enter or move concerns; empty on a leave.</summary>
        public IReadOnlyList<Peer> Peers { get; }

        /// <summary>Set on a leave.</summary>
        public string? UserId { get; }
    }

    /// <summary>Options for <see cref="PeerMap.Create"/>.</summary>
    public sealed class PeerMapOptions
    {
        /// <summary>The receiver's own userId; its entry in <c>pos</c> broadcasts is dropped.</summary>
        public string SelfUserId { get; set; } = string.Empty;
    }

    /// <summary>The set of peers visible in the current zone.</summary>
    public interface IPeerMap
    {
        /// <summary>The zone of the last snapshot, or null before one arrives.</summary>
        string? Zone { get; }

        /// <summary>Applies one frame; returns the change it produced, or null when it was ignored.</summary>
        PeerChange? Apply(LobbyServerFrame frame);

        Peer? Get(string userId);

        IReadOnlyList<Peer> All();

        void Reset();
    }

    /// <summary>
    /// Reduces the gateway's snapshot / enter / leave / pos frames into the peers
    /// visible in the current zone.
    /// </summary>
    /// <remarks>
    /// A snapshot replaces everything — that is how a zone change starts — and frames
    /// for any other zone are ignored, so a late <c>pos</c> from the old zone cannot
    /// resurrect a peer that already left.
    /// </remarks>
    public static class PeerMap
    {
        public static IPeerMap Create(PeerMapOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            return new PeerMapImpl(options.SelfUserId ?? string.Empty);
        }

        private static readonly IReadOnlyList<Peer> NoPeers = new Peer[0];

        private sealed class PeerMapImpl : IPeerMap
        {
            private readonly string _selfUserId;
            private readonly Dictionary<string, Peer> _peers = new Dictionary<string, Peer>(StringComparer.Ordinal);
            private readonly List<string> _order = new List<string>();

            internal PeerMapImpl(string selfUserId)
            {
                _selfUserId = selfUserId;
            }

            public string? Zone { get; private set; }

            public PeerChange? Apply(LobbyServerFrame frame)
            {
                if (frame == null)
                {
                    throw new ArgumentNullException(nameof(frame));
                }

                if (frame is SnapshotFrame snapshot)
                {
                    return ApplySnapshot(snapshot);
                }

                switch (frame)
                {
                    case EnterFrame enter:
                        return InZone(enter.Zone) ? ApplyEnter(enter) : null;
                    case LeaveFrame leave:
                        return InZone(leave.Zone) ? ApplyLeave(leave) : null;
                    case PosBroadcastFrame pos:
                        return InZone(pos.Zone) ? ApplyPos(pos) : null;
                    default:
                        return null;
                }
            }

            private bool InZone(string zone)
                => Zone != null && string.Equals(zone, Zone, StringComparison.Ordinal);

            private PeerChange ApplySnapshot(SnapshotFrame snapshot)
            {
                Zone = snapshot.Zone;
                _peers.Clear();
                _order.Clear();
                foreach (var peer in snapshot.Peers)
                {
                    if (!string.Equals(peer.UserId, _selfUserId, StringComparison.Ordinal))
                    {
                        Put(peer);
                    }
                }

                return new PeerChange(PeerChangeKind.Snapshot, Zone, All(), null);
            }

            private PeerChange? ApplyEnter(EnterFrame enter)
            {
                if (string.Equals(enter.Peer.UserId, _selfUserId, StringComparison.Ordinal))
                {
                    return null;
                }

                Put(enter.Peer);
                return new PeerChange(PeerChangeKind.Enter, null, new[] { enter.Peer }, enter.Peer.UserId);
            }

            private PeerChange? ApplyLeave(LeaveFrame leave)
            {
                if (!_peers.Remove(leave.UserId))
                {
                    return null;
                }

                _order.Remove(leave.UserId);
                return new PeerChange(PeerChangeKind.Leave, null, NoPeers, leave.UserId);
            }

            private PeerChange? ApplyPos(PosBroadcastFrame pos)
            {
                List<Peer>? moved = null;
                foreach (var update in pos.Peers)
                {
                    if (string.Equals(update.UserId, _selfUserId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    // A peer the map does not know is not resurrected: it left, and a
                    // coalesced pos from before that must not bring the ghost back.
                    if (!_peers.TryGetValue(update.UserId, out var existing))
                    {
                        continue;
                    }

                    var next = existing.WithPosition(update.X, update.Y, update.Dir);
                    _peers[update.UserId] = next;
                    moved ??= new List<Peer>();
                    moved.Add(next);
                }

                return moved == null ? null : new PeerChange(PeerChangeKind.Move, null, moved, null);
            }

            public Peer? Get(string userId)
                => userId != null && _peers.TryGetValue(userId, out var peer) ? peer : null;

            public IReadOnlyList<Peer> All()
            {
                var all = new List<Peer>(_order.Count);
                foreach (var userId in _order)
                {
                    all.Add(_peers[userId]);
                }

                return all;
            }

            public void Reset()
            {
                _peers.Clear();
                _order.Clear();
                Zone = null;
            }

            private void Put(Peer peer)
            {
                if (!_peers.ContainsKey(peer.UserId))
                {
                    _order.Add(peer.UserId);
                }

                _peers[peer.UserId] = peer;
            }
        }
    }
}
