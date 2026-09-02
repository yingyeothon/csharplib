using System;
using System.Threading.Tasks;
using Yingyeothon.Codec;

namespace Yingyeothon.Gamebase.Client.Tests
{
    /// <summary>A lobby client wired to a fake socket, a fake clock and a capturing log.</summary>
    internal sealed class LobbyHarness : GatewayHarness
    {
        internal LobbyHarness(Action<GatewayLobbyClientOptions>? configure = null)
        {
            var options = new GatewayLobbyClientOptions();
            Wire(options, "ch_lobby");
            configure?.Invoke(options);
            Client = GatewayLobbyClient.Create(options);
        }

        internal IGatewayLobbyClient Client { get; }

        protected override IGatewayPollable Pollable => Client;

        /// <summary>Opens the socket, delivers a hello and completes the connect.</summary>
        internal async Task<Hello> ConnectAsync(JsonValue? hello = null)
        {
            var pending = Client.ConnectAsync();
            Socket.ServerOpen();
            Socket.ServerSend(hello ?? Frames.Hello());
            Poll();
            return await pending.ConfigureAwait(false);
        }

        /// <summary>Pumps until a task settles, so a test never waits on a real clock.</summary>
        internal static async Task<T> Settle<T>(Task<T> task, IGatewayPollable pollable)
        {
            for (var i = 0; i < 100 && !task.IsCompleted; i++)
            {
                pollable.Poll();
                await Task.Yield();
            }

            return await task.ConfigureAwait(false);
        }
    }
}
