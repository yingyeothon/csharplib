#nullable enable
#if UNITY_5_3_OR_NEWER
using System;
using UnityEngine;
namespace Yingyeothon.GameKit
{
    /// <summary>Unity lifecycle pump. Bind the kit once after creating it.</summary>
    public sealed class GameKitRunner : MonoBehaviour
    {
        private IGameKit? _kit;
        /// <summary>The kit this runner owns. Replacing a live binding is refused.</summary>
        public void Bind(IGameKit kit)
        {
            if(_kit!=null)throw new InvalidOperationException("A kit is already bound");
            _kit=kit??throw new ArgumentNullException(nameof(kit));
        }
        private void Update()=>_kit?.Poll();
        private void OnApplicationPause(bool paused)=>_kit?.SetForeground(!paused);
        private void OnDestroy()
        {
            var kit=_kit;_kit=null;
            if(kit!=null)
            {
                var cleanup=new GameObject("Game kit shutdown");
                DontDestroyOnLoad(cleanup);
                cleanup.AddComponent<GameKitShutdownRunner>().Bind(kit);
            }
        }
    }
}
#endif
