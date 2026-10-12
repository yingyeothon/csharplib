#nullable enable
#if UNITY_5_3_OR_NEWER && YYT_FIREBASE_MESSAGING
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Firebase;
using Firebase.Messaging;
using UnityEngine;
using Yingyeothon.Push;

namespace Yingyeothon.GameKit.Samples
{
    /// <summary>Opt-in Android adapter; import Firebase before enabling its define.</summary>
    public sealed class FirebaseMessagingAdapter : MonoBehaviour, IKitPushTopics
    {
        private IGameKit? _kit;
        private string _project="";
        /// <summary>Call after Firebase dependency checks, kit creation and session adoption.</summary>
        public void Attach(IGameKit kit)
        {
            if(_kit!=null)throw new InvalidOperationException("Adapter is already attached");
            _kit=kit??throw new ArgumentNullException(nameof(kit));
            _project=FirebaseApp.DefaultInstance.Options.ProjectId;
            FirebaseMessaging.TokenReceived+=TokenReceived;
            FirebaseMessaging.MessageReceived+=MessageReceived;
            _=RegisterCurrentAsync();
        }
        private async Task RegisterCurrentAsync()
        {
            var token=await FirebaseMessaging.GetTokenAsync();
            if(_kit!=null)await _kit.Push.RegisterAsync(new PushRegisterOptions{Token=token,Project=_project});
        }
        private void TokenReceived(object sender,TokenReceivedEventArgs args)
        {
            var kit=_kit;
            if(kit!=null)_=kit.Push.RegisterAsync(new PushRegisterOptions{Token=args.Token,Project=_project});
        }
        private void MessageReceived(object sender,MessageReceivedEventArgs args)
            =>_kit?.Push.Receive(args.Message.Data.ToDictionary(x=>x.Key,x=>x.Value));
        public Task SubscribeAsync(string topic,CancellationToken cancellationToken)
        {cancellationToken.ThrowIfCancellationRequested();return FirebaseMessaging.SubscribeAsync(topic);}
        public Task UnsubscribeAsync(string topic,CancellationToken cancellationToken)
        {cancellationToken.ThrowIfCancellationRequested();return FirebaseMessaging.UnsubscribeAsync(topic);}
        private void OnDestroy()
        {
            FirebaseMessaging.TokenReceived-=TokenReceived;
            FirebaseMessaging.MessageReceived-=MessageReceived;
            _kit=null;
        }
    }
}
#endif
