using System;
using System.Collections.Generic;
using Yingyeothon.Codec;
using Yingyeothon.Logger;

namespace Yingyeothon.Gamebase.Client
{
    /// <summary>What applying a frame did to the peer map.</summary>
    public enum PeerChangeKind
    {
        /// <summary>The zone was replaced wholesale.</summary>
        Snapshot,
        /// <summary>One peer became visible.</summary>
        Enter,
        /// <summary>One peer stopped being visible.</summary>
        Leave,
        /// <summary>One or more known peers moved.</summary>
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

        /// <summary>What kind of change the frame produced.</summary>
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

        /// <summary>
        /// Receives a <c>Warn</c> for a view-invariant break: a <c>pos</c> or <c>leave</c> for
        /// a peer this map never saw enter. The gateway promises that cannot happen, so it
        /// is a gateway bug, not a client one, and silence is what would let it stay a
        /// rendering oddity nobody can trace. Each peer is reported once per zone, with its
        /// id and the zone only. Null is <c>NullLogger.Instance</c>. The lobby client passes
        /// its own.
        /// </summary>
        public ILogger? Logger { get; set; }
    }

    /// <summary>The set of peers visible in the current zone.</summary>
    public interface IPeerMap
    {
        /// <summary>The zone of the last snapshot, or null before one arrives.</summary>
        string? Zone { get; }

        /// <summary>Applies one frame; returns the change it produced, or null when it was ignored.</summary>
        PeerChange? Apply(LobbyServerFrame frame);

        /// <summary>One peer, or null when the map does not know it.</summary>
        Peer? Get(string userId);

        /// <summary>Every peer currently visible, in the order they arrived.</summary>
        IReadOnlyList<Peer> All();

        /// <summary>Forgets every peer and the current zone. The client does this on every disconnect.</summary>
        void Reset();
    }

    /// <summary>
    /// Reduces the gateway's snapshot / enter / leave / pos frames into the peers
    /// visible in the current zone.
    /// </summary>
    /// <remarks>
    /// A snapshot replaces everything — that is how a zone change starts — and frames
    /// for any other zone are ignored, so a late <c>pos</c> from the old zone cannot
    /// resurrect a peer that already left. A frame for the current zone naming a peer the
    /// map never saw is different: the gateway's view invariant says it cannot happen, so
    /// it is ignored for rendering <b>and</b> logged through <see cref="PeerMapOptions.Logger"/>.
    /// </remarks>
    public static class PeerMap
    {
        /// <summary>Creates a peer map that filters out the receiver's own entries.</summary>
        public static IPeerMap Create(PeerMapOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            return new PeerMapImpl(options.SelfUserId ?? string.Empty, options.Logger ?? NullLogger.Instance);
        }

        private static readonly IReadOnlyList<Peer> NoPeers = new Peer[0];

        private sealed class PeerMapImpl : IPeerMap
        {
            private readonly string _selfUserId;
            private readonly ILogger _logger;
            private readonly Dictionary<string, Peer> _peers = new Dictionary<string, Peer>(StringComparer.Ordinal);
            private readonly List<string> _order = new List<string>();

            // The ids already reported in this zone. A gateway bug repeats every tick —
            // up to 256 entries a batch, five batches a second — and a log line per entry
            // per batch would be the amplifier, so each id is reported once per zone and
            // the set itself is capped. The key is the bounded diagnostic form, never the
            // raw id: a hostile id can be as long as a frame.
            private readonly HashSet<string> _reported = new HashSet<string>(StringComparer.Ordinal);
            private bool _reportCapHit;
            private const int MaxReported = 256;

            internal PeerMapImpl(string selfUserId, ILogger logger)
            {
                _selfUserId = selfUserId;
                _logger = logger;
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
                _reported.Clear();
                _reportCapHit = false;
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
                if (string.Equals(leave.UserId, _selfUserId, StringComparison.Ordinal))
                {
                    return null;
                }

                if (!_peers.Remove(leave.UserId))
                {
                    // Ignored for rendering, but said: the gateway promises a `leave`
                    // only for a peer it introduced first.
                    WarnUnknown("leave for an unknown peer", leave.UserId, leave.Zone);
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

                    // A peer the map does not know is not resurrected. The gateway sends
                    // `pos` only for a peer in view, so this is its bug: say so.
                    if (!_peers.TryGetValue(update.UserId, out var existing))
                    {
                        WarnUnknown("pos for an unknown peer", update.UserId, pos.Zone);
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
                _reported.Clear();
                _reportCapHit = false;
                Zone = null;
            }

            /// <summary>
            /// Once per peer per zone. The ids are routing facts, never the frame, and are
            /// capped and stripped anyway: a zone name is chosen by whichever player
            /// announced it, and a hostile gateway chooses both.
            /// </summary>
            private void WarnUnknown(string message, string userId, string zone)
            {
                var id = Normalize.Diagnostic(userId);
                if (_reported.Count >= MaxReported)
                {
                    if (!_reportCapHit && !_reported.Contains(id))
                    {
                        _reportCapHit = true;
                        _logger.Warn(
                            "unknown peers past the report cap are not logged in this zone",
                            Json.Object().Set("zone", Normalize.Diagnostic(zone)).Set("reported", (double)MaxReported).Build());
                    }

                    return;
                }

                if (!_reported.Add(id))
                {
                    return;
                }

                _logger.Warn(
                    message,
                    Json.Object()
                        .Set("userId", id)
                        .Set("zone", Normalize.Diagnostic(zone))
                        .Build());
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
