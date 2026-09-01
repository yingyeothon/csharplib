#if UNITY_5_3_OR_NEWER
using System.Collections.Generic;
using UnityEngine;

namespace Yingyeothon.Gamebase.Client.Samples
{
    /// <summary>
    /// The whole of a working lobby client. Set the two ids in the inspector, call
    /// <see cref="Begin"/> with a channel JWT, and poll.
    /// </summary>
    public sealed class LobbyQuickstart : MonoBehaviour
    {
        [SerializeField] private string url = "wss://gw.yyt.life";
        [SerializeField] private string channelId = "lobby_0123456789abcdef";

        private LobbySession _session;

        public async void Begin(string channelJwt)
        {
            _session = new LobbySession(url, channelId, channelJwt);

            _session.PeerEntered += peer => Debug.Log($"enter {peer.UserId} at {peer.X},{peer.Y}");
            _session.PeerLeft += userId => Debug.Log($"leave {userId}");
            _session.PeersMoved += Move;
            _session.ChatArrived += frame => Debug.Log($"{frame.From}: {frame.Text}");
            _session.Dropped += e => Debug.Log($"dropped {e.Code}, reconnecting={e.WillReconnect}");
            _session.Ended += e => Debug.LogWarning($"stopped: {e.Kind} ({e.Code})");

            // Announce the position on every connect, this one and every reconnect.
            _session.Connected += _ => _session.Move(transform.position.x, transform.position.z, "n");

            var hello = await _session.ConnectAsync();
            Debug.Log($"connected as {hello.UserId} in {hello.Zone}, tick {hello.Tick} ms");
        }

        // Unconditionally, and before any pause or timeScale check.
        private void Update() => _session?.Poll();

        private void OnDestroy() => _session?.Dispose();

        private static void Move(IReadOnlyList<Peer> peers)
        {
            foreach (var peer in peers)
            {
                // peer.X, peer.Y, peer.Dir — your own entry is already filtered out.
                Debug.Log($"move {peer.UserId} to {peer.X},{peer.Y} facing {peer.Dir}");
            }
        }
    }
}
#endif
