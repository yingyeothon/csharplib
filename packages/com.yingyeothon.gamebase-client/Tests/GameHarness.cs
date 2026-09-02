using System;
using System.Threading.Tasks;

namespace Yingyeothon.Gamebase.Client.Tests
{
    /// <summary>A dungeon client wired to a fake socket, a fake clock and a capturing log.</summary>
    internal sealed class GameHarness : GatewayHarness
    {
        internal GameHarness(Action<GatewayGameClientOptions>? configure = null)
        {
            var options = new GatewayGameClientOptions { GameId = "g_1" };
            Wire(options, "q_dungeon");
            configure?.Invoke(options);
            Client = GatewayGameClient.Create(options);
        }

        internal IGatewayGameClient Client { get; }

        protected override IGatewayPollable Pollable => Client;

        /// <summary>A q channel is ready on open; there is no hello handshake.</summary>
        internal async Task ConnectAsync()
        {
            var pending = Client.ConnectAsync();
            Socket.ServerOpen();
            Poll();
            await pending.ConfigureAwait(false);
        }
    }
}
