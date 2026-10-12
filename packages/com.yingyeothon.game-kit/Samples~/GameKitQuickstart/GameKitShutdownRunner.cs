#nullable enable
#if UNITY_5_3_OR_NEWER
using System.Threading.Tasks;
using UnityEngine;
namespace Yingyeothon.GameKit
{
    /// <summary>Temporary persistent pump while asynchronous shutdown settles.</summary>
    public sealed class GameKitShutdownRunner:MonoBehaviour
    {
        private IGameKit? _kit;
        private Task? _shutdown;
        public void Bind(IGameKit kit){_kit=kit;_shutdown=kit.ShutdownAsync();}
        private void Update(){if(_kit==null)return;_kit.Poll();if(_shutdown?.IsCompleted==true){_kit=null;Destroy(gameObject);}}
    }
}
#endif
