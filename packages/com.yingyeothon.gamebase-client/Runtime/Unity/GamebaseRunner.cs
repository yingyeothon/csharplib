#if UNITY_5_3_OR_NEWER
using System.Collections.Generic;
using UnityEngine;

namespace Yingyeothon.Gamebase.Client
{
    /// <summary>
    /// Pumps gateway clients from Unity's main thread.
    /// </summary>
    /// <remarks>
    /// Nothing a client received is observed until it is polled, and its timeouts and
    /// reconnect delays only advance there — so <c>Poll()</c> must run every frame,
    /// unconditionally, before any pause or <c>timeScale</c> check. Attach one runner
    /// and register every client with it, or call <c>Poll()</c> yourself.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class GamebaseRunner : MonoBehaviour
    {
        private readonly List<IGatewayPollable> _pollables = new List<IGatewayPollable>();
        private readonly List<IGatewayPollable> _snapshot = new List<IGatewayPollable>();

        /// <summary>Creates a runner that survives scene loads.</summary>
        public static GamebaseRunner CreatePersistent(string name = "GamebaseRunner")
        {
            var host = new GameObject(name);
            DontDestroyOnLoad(host);
            return host.AddComponent<GamebaseRunner>();
        }

        public void Add(IGatewayPollable pollable)
        {
            if (pollable != null && !_pollables.Contains(pollable))
            {
                _pollables.Add(pollable);
            }
        }

        public void Remove(IGatewayPollable pollable) => _pollables.Remove(pollable);

        private void Update()
        {
            // Poll a copy: a handler may add or remove a client while it runs, and a
            // dungeon client is usually created from a lobby handler.
            _snapshot.Clear();
            _snapshot.AddRange(_pollables);
            foreach (var pollable in _snapshot)
            {
                pollable.Poll();
            }
        }
    }
}
#endif
