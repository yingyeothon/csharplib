#if UNITY_5_3_OR_NEWER
using UnityEngine;
using Yingyeothon.Codec;

namespace Yingyeothon.KvStore.Samples
{
    /// <summary>
    /// Reads the notice board and round-trips the player's settings. Set the base URL
    /// in the inspector and call <see cref="Begin"/> with the channel JWT your sign-in
    /// produced.
    /// </summary>
    public sealed class KvStoreQuickstart : MonoBehaviour
    {
        [SerializeField] private string baseUrl = "https://doc.yyt.life";

        private KvStoreSession _session;

        public async void Begin(string channelJwt)
        {
            // HttpClient cannot send on WebGL; UnityWebRequest can, on every platform.
#if UNITY_WEBGL && !UNITY_EDITOR
            _session = new KvStoreSession(baseUrl, channelJwt, UnityWebRequestTransport.Instance);
#else
            _session = new KvStoreSession(baseUrl, channelJwt);
#endif

            try
            {
                foreach (var notice in await _session.ReadAnnouncementsAsync())
                {
                    // notice.Value is the JSON the team published; log its key, never the value.
                    Debug.Log($"announcement {notice.Key} v{notice.Version}");
                }

                var saved = await _session.LoadSettingsAsync();
                var volume = saved?.GetNumber("volume") ?? 1.0;
                await _session.SaveSettingsAsync(Json.Object().Set("volume", volume * 0.5).Build());

                Debug.Log($"plays so far: {await _session.CountPlayAsync()}");
            }
            catch (KvStoreException error)
            {
                // The message is "kv {code} ({status})" and safe to print as it is.
                Debug.LogWarning(error.Message);
            }
        }
    }
}
#endif
