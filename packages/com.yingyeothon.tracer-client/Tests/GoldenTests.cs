using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using NUnit.Framework;
using Yingyeothon.Codec;

namespace Yingyeothon.Tracer.Tests
{
    /// <summary>
    /// The golden envelope of this library. Its bytes are also the service repository's
    /// <c>tracer/testdata/envelopes/yyt-csharp/report.envelope</c>, where tracer's own tests
    /// ingest it. Regenerate with <c>UPDATE_GOLDEN=1 dotnet test --filter GoldenTests</c>,
    /// then copy the file there unchanged.
    /// </summary>
    [TestFixture]
    public class GoldenTests
    {
        /// <summary>A 1x1 transparent PNG.</summary>
        internal static readonly byte[] OnePixelPng = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

        private const string StackTrace =
            "Board.Move (System.String square) (at Assets/Scripts/Board.cs:42)\n"
            + "BoardInput:OnTap (UnityEngine.EventSystems.PointerEventData data) (at Assets/Scripts/BoardInput.cs:88)\n"
            + "UnityEngine.EventSystems.ExecuteEvents:Execute (UnityEngine.EventSystems.IPointerClickHandler,UnityEngine.EventSystems.BaseEventData) (at ./Library/PackageCache/com.unity.ugui@1.0.0/Runtime/EventSystem/ExecuteEvents.cs:57)\n";

        /// <summary>What a Unity game's "report this" sends, with fixed clocks and ids.</summary>
        internal static byte[] GoldenReport()
        {
            var harness = new Harness(o =>
            {
                o.Tags = new Dictionary<string, string> { ["git_sha"] = "abc1234", ["engine"] = "unity" };
                o.Contexts = Json.Object()
                    .Set("os", Json.Object().Set("name", "Android").Set("version", "15").Build())
                    .Set("device", Json.Object().Set("model", "fixture-device").Build())
                    .Build();
                o.Logger = null;
            });
            var c = harness.Client;
            c.SetTag("feature", "board");
            c.Trace("scene", "loaded Board", Json.Object().Set("from", "Title").Set("to", "Board").Build(), TracerLevel.Info);
            c.Trace("ui", "tap square", Json.Object().Set("square", "e2").Set("piece", 1).Set("legal", false).Build(), TracerLevel.Info);
            c.Log(TracerLevel.Warning, "move rejected", Json.Object().Set("square", "e2").Set("ms", 12.5).Set("attempt", 2).Set("legal", false).Build());
            c.Report("capture report: rejected move", new TracerReportOptions
            {
                Data = Json.Object().Set("board", "8/8/8/8/8/8/4P3/8").Build(),
                Screenshot = OnePixelPng,
                Attachments = new[]
                {
                    TracerAttachment.ViewHierarchy(Encoding.UTF8.GetBytes(
                        "{\"rendering_system\":\"unity\",\"windows\":[{\"type\":\"Canvas\",\"depth\":1,\"children\":[]}]}")),
                },
                Exception = new InvalidOperationException("rejected move e2"),
                StackTrace = StackTrace,
            });
            c.Poll();
            Assert.That(harness.Transport.Sent.Count, Is.EqualTo(1));
            return harness.Transport.Sent[0].Body;
        }

        internal static string Locate(string relative)
        {
            var built = Path.Combine(TestContext.CurrentContext.TestDirectory, relative);
            return File.Exists(built)
                ? built
                : Path.Combine("Packages", "com.yingyeothon.tracer-client", relative.StartsWith("Golden", StringComparison.Ordinal) ? Path.Combine("Tests", relative) : relative);
        }

        private static string SourceGolden([CallerFilePath] string here = "")
            => Path.Combine(Path.GetDirectoryName(here)!, "Golden", "report.envelope");

        [Test]
        public void TheReportEnvelopeIsByteForByteTheGoldenFile()
        {
            var body = GoldenReport();
            if (Environment.GetEnvironmentVariable("UPDATE_GOLDEN") == "1")
            {
                File.WriteAllBytes(SourceGolden(), body);
                Assert.Ignore("golden rewritten");
            }

            var golden = File.ReadAllBytes(Locate(Path.Combine("Golden", "report.envelope")));
            Assert.That(Encoding.UTF8.GetString(body), Is.EqualTo(Encoding.UTF8.GetString(golden)));
            Assert.That(body, Is.EqualTo(golden));
        }

        // The facts tracer stores of it (normalize.Event/Logs, ingest's attachment
        // linkage); the Go side counts its rows (internal/ingest TestGoldenEnvelopes).
        [Test]
        public void TheGoldenDecodesToWhatTracerStores()
        {
            var env = ParsedEnvelope.Parse(File.ReadAllBytes(Locate(Path.Combine("Golden", "report.envelope"))));
            Assert.That(env.Header.AsObject().Select(m => m.Key).ToArray(), Is.EqualTo(new[] { "event_id", "sent_at", "sdk" }));
            Assert.That(env.Header.GetMemberOrNull("sdk"), Is.EqualTo(Json.Object().Set("name", "yyt.csharp").Set("version", TracerClient.SdkVersion).Build()));
            Assert.That(env.Items.Select(i => i.Type + ":" + (i.Header.GetString("filename") ?? string.Empty)).ToArray(), Is.EqualTo(new[]
            {
                "event:", "log:", "attachment:screenshot.png", "attachment:view-hierarchy.json",
            }));

            var e = env.Items[0].Json;
            Assert.That(e.GetString("event_id"), Is.EqualTo(env.Header.GetString("event_id")));
            Assert.That(e.GetString("event_id"), Is.EqualTo("404142434445464748494a4b4c4d4e4f"));
            Assert.That(e.GetString("level"), Is.EqualTo("warning"));
            Assert.That(e.GetString("platform"), Is.EqualTo("csharp"));
            Assert.That(e.GetString("release"), Is.EqualTo("playground@0.1.0+1"));
            Assert.That(e.GetString("dist"), Is.EqualTo("abc1234"));
            Assert.That(e.GetString("environment"), Is.EqualTo("debug"));
            Assert.That(e.GetString("timestamp"), Does.Match(@"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d\.\d{3}Z$"));
            Assert.That(e.GetMemberOrNull("user"), Is.EqualTo(Json.Object().Set("id", "00000000000000000000000000000000").Build()));
            Assert.That(e.GetMemberOrNull("tags"), Is.EqualTo(Json.Object().Set("git_sha", "abc1234").Set("engine", "unity").Set("feature", "board").Build()));
            var contexts = e.GetMemberOrNull("contexts")!;
            Assert.That(contexts.GetMemberOrNull("device")!.GetString("model"), Is.EqualTo("fixture-device"));
            Assert.That(contexts.GetMemberOrNull("trace")!.GetString("trace_id"), Is.EqualTo("303132333435363738393a3b3c3d3e3f"));

            var exception = e.GetMemberOrNull("exception")!.GetArrayOrEmpty("values")[0];
            Assert.That(exception.GetString("type"), Is.EqualTo("System.InvalidOperationException"));
            Assert.That(exception.GetString("value"), Is.EqualTo("rejected move e2"));
            var frames = exception.GetMemberOrNull("stacktrace")!.GetArrayOrEmpty("frames");
            Assert.That(frames.Select(f => f.GetBool("in_app")).ToArray(), Is.EqualTo(new bool?[] { false, true, true }));
            Assert.That(frames.Select(f => f.GetString("filename")).ToArray(), Is.EqualTo(new[]
            {
                "ExecuteEvents.cs", "Assets/Scripts/BoardInput.cs", "Assets/Scripts/Board.cs",
            }));
            Assert.That(frames[2].GetString("function"), Is.EqualTo("Board.Move"));
            Assert.That(frames[2].GetNumber("lineno"), Is.EqualTo(42));
            Assert.That(frames[1].GetString("function"), Is.EqualTo("BoardInput.OnTap"));
            Assert.That(e.GetMemberOrNull("breadcrumbs")!.GetArrayOrEmpty("values").Count, Is.EqualTo(2));

            var logs = env.Items[1];
            Assert.That(logs.Header.GetNumber("item_count"), Is.EqualTo(3));
            Assert.That(logs.Header.GetString("content_type"), Is.EqualTo("application/vnd.sentry.items.log+json"));
            var entries = logs.Json.GetArrayOrEmpty("items");
            Assert.That(entries.Select(x => x.GetString("body")).ToArray(), Is.EqualTo(new[] { "loaded Board", "tap square", "move rejected" }));
            Assert.That(entries[2].GetString("level"), Is.EqualTo("warn"));
            Assert.That(entries[2].GetNumber("severity_number"), Is.EqualTo(13));
            Assert.That(entries[2].GetMemberOrNull("timestamp")!.Kind, Is.EqualTo(JsonKind.Number));
            var attrs = entries[2].GetMemberOrNull("attributes")!;
            Assert.That(attrs.GetMemberOrNull("square"), Is.EqualTo(Typed(JsonValue.Of("e2"), "string")));
            Assert.That(attrs.GetMemberOrNull("ms"), Is.EqualTo(Typed(JsonValue.Of(12.5), "double")));
            Assert.That(attrs.GetMemberOrNull("attempt"), Is.EqualTo(Typed(JsonValue.Of(2), "integer")));
            Assert.That(attrs.GetMemberOrNull("legal"), Is.EqualTo(Typed(JsonValue.Of(false), "boolean")));
            Assert.That(attrs.GetMemberOrNull("sentry.release"), Is.EqualTo(Typed(JsonValue.Of("playground@0.1.0+1"), "string")));
            Assert.That(attrs.GetMemberOrNull("device.model"), Is.EqualTo(Typed(JsonValue.Of("fixture-device"), "string")));

            Assert.That(env.Items[2].Payload, Is.EqualTo(OnePixelPng));
            Assert.That(env.Items[2].Header.GetString("content_type"), Is.EqualTo("image/png"));
            Assert.That(env.Items[3].Header.GetString("attachment_type"), Is.EqualTo("event.view_hierarchy"));
        }

        [Test]
        public void TheSdkVersionIsThePackageVersion()
        {
            var manifest = Json.Parse(File.ReadAllText(Locate("package.json")));
            Assert.That(TracerClient.SdkVersion, Is.EqualTo(manifest.GetString("version")));
        }

        private static JsonValue Typed(JsonValue value, string type) => Json.Object().Set("value", value).Set("type", type).Build();
    }
}
