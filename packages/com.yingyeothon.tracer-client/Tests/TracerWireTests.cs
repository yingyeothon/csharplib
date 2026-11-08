using System.Linq;
using NUnit.Framework;
using Yingyeothon.Codec;

namespace Yingyeothon.Tracer.Tests
{
    /// <summary>The stack-trace shapes the three runtimes print, read through a report.</summary>
    [TestFixture]
    public class TracerWireTests
    {
        private static JsonValue[] Frames(string stackTrace)
        {
            var h = new Harness();
            h.Client.Report("x", new TracerReportOptions { StackTrace = stackTrace });
            h.Client.Poll();
            var value = h.Transport.Sent.Single().Envelope.Items[0].Json.GetMemberOrNull("exception")!.GetArrayOrEmpty("values")[0];
            Assert.That(value.GetString("type"), Is.EqualTo("Exception"), "a bare stack trace is an untyped exception");
            return value.GetMemberOrNull("stacktrace")?.GetArrayOrEmpty("frames").ToArray() ?? new JsonValue[0];
        }

        [Test]
        public void DotNetFramesKeepFileAndLine()
        {
            var frames = Frames(
                "   at Game.Board.Move(String square) in /home/dev/game/Assets/Scripts/Board.cs:line 42\n"
                + "   at System.Threading.Tasks.Task.Run()\n");
            Assert.That(frames.Length, Is.EqualTo(2));
            Assert.That(frames[0].GetString("function"), Is.EqualTo("System.Threading.Tasks.Task.Run"));
            Assert.That(frames[0].GetBool("in_app"), Is.False);
            Assert.That(frames[0].GetMemberOrNull("filename"), Is.Null);
            Assert.That(frames[1].GetString("function"), Is.EqualTo("Game.Board.Move"));
            Assert.That(frames[1].GetString("filename"), Is.EqualTo("Assets/Scripts/Board.cs"));
            Assert.That(frames[1].GetMemberOrNull("abs_path"), Is.Null, "a build-machine path names the developer");
            Assert.That(frames[1].GetNumber("lineno"), Is.EqualTo(42));
            Assert.That(frames[1].GetBool("in_app"), Is.True);
            Assert.That(frames[1].GetString("platform"), Is.EqualTo("csharp"));
        }

        [Test]
        public void MonoFramesDropTheIlOffsetAndAnUnresolvedFile()
        {
            var frames = Frames(
                "  at Game.Board.Move (System.String square) [0x00012] in /src/Board.cs:42 \n"
                + "  at Game.Input.Tap () [0x00000] in <00000000000000000000000000000000>:0 \n");
            Assert.That(frames.Select(f => f.GetString("function")).ToArray(), Is.EqualTo(new[] { "Game.Input.Tap", "Game.Board.Move" }));
            Assert.That(frames[0].GetMemberOrNull("filename"), Is.Null);
            Assert.That(frames[0].GetMemberOrNull("lineno"), Is.Null);
            Assert.That(frames[1].GetString("filename"), Is.EqualTo("Board.cs"));
            Assert.That(frames[1].GetNumber("lineno"), Is.EqualTo(42));
        }

        [Test]
        public void MonoWrapperFramesAreRuntimeGlue()
        {
            var frames = Frames(
                "  at (wrapper managed-to-native) UnityEngine.Object.Destroy(UnityEngine.Object,single)\n"
                + "  at Game.Board.Clear () [0x00001] in /src/Board.cs:7 \n");
            Assert.That(frames.Select(f => f.GetString("function")).ToArray(), Is.EqualTo(new[] { "Game.Board.Clear", "UnityEngine.Object.Destroy" }));
            Assert.That(frames[1].GetBool("in_app"), Is.False);
        }

        [Test]
        public void UnityLogFramesUseTheTypeColonMethodShape()
        {
            var frames = Frames(
                "UnityEngine.Debug:LogException (System.Exception)\n"
                + "Game.Board:Move (string) (at Assets/Scripts/Board.cs:42)\n"
                + "--- End of stack trace ---\n"
                + "nonsense\n");
            Assert.That(frames.Length, Is.EqualTo(1));
            Assert.That(frames[0].GetString("function"), Is.EqualTo("Game.Board.Move"));
            Assert.That(frames[0].GetString("filename"), Is.EqualTo("Assets/Scripts/Board.cs"));
        }

        [Test]
        public void TextIsClippedOnACharacterBoundary()
        {
            var h = new Harness();
            var message = new string('a', TracerLimits.MaxTextBytes - 1) + "\U0001F600";
            h.Client.Report(message);
            h.Client.Poll();
            var sent = h.Transport.Sent.Single().Envelope.Items[0].Json.GetString("message")!;
            Assert.That(sent, Is.EqualTo(new string('a', TracerLimits.MaxTextBytes - 1)), "a pair that does not fit is dropped whole");
        }

        [Test]
        public void DeepOrWideDataIsBounded()
        {
            var h = new Harness();
            JsonValue deep = JsonValue.Of(1);
            for (var i = 0; i < 20; i++)
            {
                deep = Json.Object().Set("d", deep).Build();
            }

            var wide = JsonValue.Array(Enumerable.Range(0, 300).Select(i => JsonValue.Of(i)));
            h.Client.Report("x", new TracerReportOptions { Data = Json.Object().Set("deep", deep).Set("wide", wide).Build() });
            h.Client.Poll();
            var extra = h.Transport.Sent.Single().Envelope.Items[0].Json.GetMemberOrNull("extra")!;
            Assert.That(extra.GetArrayOrEmpty("wide").Count, Is.EqualTo(200));
            var node = extra.GetMemberOrNull("deep")!;
            var depth = 1;
            while (node.Kind == JsonKind.Object)
            {
                node = node.GetMemberOrNull("d")!;
                depth++;
            }

            Assert.That(node.AsString(), Is.EqualTo("…"));
            Assert.That(depth, Is.EqualTo(8));
        }
    }
}
