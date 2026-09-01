namespace Yingyeothon.Gamebase.Client
{
    /// <summary>The two channel kinds the gateway terminates.</summary>
    public enum GatewayChannelKind
    {
        /// <summary>Positions, chat, parties and game events.</summary>
        Lobby,

        /// <summary>The dungeon bridge to a lambda-gamebase actor.</summary>
        Q,
    }
}
