# Android push

The game obtains an FCM token and supplies its Firebase project id. Register on
startup and token refresh. game-kit subscribes through IKitPushTopics only after
registration succeeds, using the exact topic from kit-config. Explicit sign-out
removes the registration and unsubscribes before discarding the session.

Pass both foreground data messages and notification-tap data to Push.Receive.
A match message triggers a ticket GET; campaigns and broadcasts raise MessageReceived.
Resume also refreshes a deferred ticket. Topic messages are public and grant no
rights. Never log a device token, player JWT or message body. iOS is unsupported.

Real Android receipt requires the host's Firebase configuration and an actual device;
fake adapter tests cannot establish that it works on a device.

The game-kit Firebase Messaging sample uses the [official Unity SDK API](https://firebase.google.com/docs/reference/unity/class/firebase/messaging/firebase-messaging).
Import the SDK and sample, then enable YYT_FIREBASE_MESSAGING. Its separate asmdef
is disabled by default. Real SDK compilation and device receipt require the host setup.
