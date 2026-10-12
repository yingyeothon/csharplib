using Yingyeothon.Codec;
using Yingyeothon.GameKit;
namespace Yingyeothon.GameKit.Samples
{
    /// <summary>Engine-free native sample factory. WebGL hosts supply transports through GameKitOptions.</summary>
    public static class GameKitSession
    {
        public static IGameKit Create(JsonValue config)
        {
            return GameKit.Create(new GameKitOptions { Config = config });
        }
    }
}
