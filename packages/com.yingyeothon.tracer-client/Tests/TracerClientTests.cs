using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Yingyeothon.Codec;
using Yingyeothon.Logger;

namespace Yingyeothon.Tracer.Tests
{
    [TestFixture]
    public class TracerClientTests
    {
        private static int Dropped(ITracerClient c, string reason)
            => c.Stats.Dropped.TryGetValue(reason, out var n) ? n : 0;

        [Test]
        public void AnEmptyDsnGivesAClientThatIsOffAndDoesNothing()
        {
            var h = new Harness(o => o.Dsn = "");
            Assert.That(h.Client.State, Is.EqualTo(TracerState.Off));
            Assert.That(h.Client.Enabled, Is.False);
            h.Client.Trace("ui", "tap");
            Assert.That(h.Client.Report("x"), Is.Null);
            Assert.That(h.Client.Feedback("x", null), Is.Null);
            Assert.That(h.Client.FlushAsync().IsCompleted, Is.True);
            h.Advance(60_000);
            Assert.That(h.Transport.Sent.Count, Is.EqualTo(0));
            Assert.That(h.Log.Lines.Count, Is.EqualTo(0), "an empty DSN is the normal way to turn it off");
        }

        [Test]
        public void ABadDsnIsOffWithOneFixedLineThatNeverQuotesIt()
        {
            var bad = "https://fixturewritekeyfixturewritekey00@trace.invalid/not-a-resource";
            var h = new Harness(o => o.Dsn = bad);
            Assert.That(h.Client.State, Is.EqualTo(TracerState.Off));
            Assert.That(h.Log.Lines.Count, Is.EqualTo(1));
            Assert.That(h.Log.Text, Does.Contain("tracer off: DSN did not parse"));
            Assert.That(h.Log.Text, Does.Not.Contain("fixturewritekey"));
            Assert.That(h.Log.Text, Does.Not.Contain("not-a-resource"));
        }

        [Test]
        public void NothingIsSentUntilPoll()
        {
            var h = new Harness();
            Assert.That(h.Client.Report("x"), Is.Not.Null);
            Assert.That(h.Transport.Sent.Count, Is.EqualTo(0));
            Assert.That(h.Client.Stats.Queued, Is.EqualTo(1));
            h.Client.Poll();
            Assert.That(h.Transport.Sent.Count, Is.EqualTo(1));
            Assert.That(h.Client.Stats.EnvelopesSent, Is.EqualTo(1));
            Assert.That(h.Client.Stats.Queued, Is.EqualTo(0));
        }

        [Test]
        public void ARequestCarriesTheContractHeadersAndAGzippedBody()
        {
            var h = new Harness();
            h.Client.Report("x");
            h.Client.Poll();
            var sent = h.Transport.Sent.Single();
            Assert.That(sent.Request.Url.AbsoluteUri, Is.EqualTo("https://trace.invalid/api/trc_fixture01/envelope/"));
            Assert.That(sent.Request.Headers.Select(x => x.Key).ToArray(), Is.EqualTo(new[] { "Content-Type", "X-Sentry-Auth", "Content-Encoding" }));
            Assert.That(sent.Header("Content-Type"), Is.EqualTo("application/x-sentry-envelope"));
            Assert.That(sent.Header("X-Sentry-Auth"), Is.EqualTo(
                "Sentry sentry_key=" + Fixture.WriteKey + ", sentry_version=7, sentry_client=yyt-csharp/" + TracerClient.SdkVersion));
            Assert.That(sent.Header("Content-Encoding"), Is.EqualTo("gzip"));
            Assert.That(sent.Request.Body[0], Is.EqualTo(0x1f), "the body as sent is gzip");
            Assert.That(sent.Request.Timeout, Is.EqualTo(TimeSpan.FromSeconds(30)));
            Assert.That(sent.Envelope.Header.GetMemberOrNull("dsn"), Is.Null, "the envelope header never repeats the key");
            Assert.That(Encoding.UTF8.GetString(sent.Body), Does.Not.Contain(Fixture.WriteKey));
        }

        [Test]
        public void CompressFalseSendsThePlainEnvelope()
        {
            var h = new Harness(o => o.Compress = false);
            h.Client.Report("x");
            h.Client.Poll();
            var sent = h.Transport.Sent.Single();
            Assert.That(sent.Header("Content-Encoding"), Is.Null);
            Assert.That(sent.Request.Body, Is.EqualTo(sent.Body));
        }

        [Test]
        public void AReportCarriesTheRingTheBufferedLogsAndItsFiles()
        {
            var h = new Harness();
            h.Client.Breadcrumb("nav", "open");
            h.Client.Log(TracerLevel.Info, "line");
            var id = h.Client.Report("boom", new TracerReportOptions
            {
                Screenshot = GoldenTests.OnePixelPng,
                Attachments = new[] { new TracerAttachment("save.json", new byte[] { 1, 2 }, "application/json", "event.attachment") },
                Level = TracerLevel.Error,
            });
            h.Client.Poll();
            var env = h.Transport.Sent.Single().Envelope;
            Assert.That(env.Header.GetString("event_id"), Is.EqualTo(id));
            Assert.That(env.Items.Select(i => i.Type).ToArray(), Is.EqualTo(new[] { "event", "log", "attachment", "attachment" }));
            var e = env.Items[0].Json;
            Assert.That(e.GetString("level"), Is.EqualTo("error"));
            Assert.That(e.GetString("message"), Is.EqualTo("boom"));
            Assert.That(e.GetMemberOrNull("breadcrumbs")!.GetArrayOrEmpty("values")[0].GetString("message"), Is.EqualTo("open"));
            Assert.That(e.GetMemberOrNull("exception"), Is.Null);
            Assert.That(e.GetMemberOrNull("extra"), Is.Null);
            Assert.That(env.Items[3].Header.GetString("filename"), Is.EqualTo("save.json"));
            Assert.That(env.Items[3].Payload, Is.EqualTo(new byte[] { 1, 2 }));
        }

        [Test]
        public void TheBreadcrumbRingKeepsTheNewest()
        {
            var h = new Harness(o => o.BreadcrumbCapacity = 2);
            h.Client.Breadcrumb("a", "1");
            h.Client.Breadcrumb("a", "2");
            h.Client.Breadcrumb("a", "3");
            h.Client.Report("x");
            h.Client.Poll();
            var crumbs = h.Transport.Sent.Single().Envelope.Items[0].Json.GetMemberOrNull("breadcrumbs")!.GetArrayOrEmpty("values");
            Assert.That(crumbs.Select(c => c.GetString("message")).ToArray(), Is.EqualTo(new[] { "2", "3" }));
        }

        [Test]
        public void ALogLineWaitsForTheFlushIntervalThenGoesAlone()
        {
            var h = new Harness();
            h.Client.Log(TracerLevel.Debug, "tick");
            h.Advance(4999);
            Assert.That(h.Transport.Sent.Count, Is.EqualTo(0));
            h.Advance(1);
            var env = h.Transport.Sent.Single().Envelope;
            Assert.That(env.Header.GetMemberOrNull("event_id"), Is.Null);
            Assert.That(env.Items.Single().Type, Is.EqualTo("log"));
            var entry = env.Items[0].Json.GetArrayOrEmpty("items").Single();
            Assert.That(entry.GetString("level"), Is.EqualTo("debug"));
            Assert.That(entry.GetNumber("severity_number"), Is.EqualTo(5));
            Assert.That(entry.GetString("trace_id"), Is.EqualTo(h.Client.TraceId));
        }

        [Test]
        public void TheBatchSizeQueuesLogLinesWithoutWaiting()
        {
            var h = new Harness(o => o.LogBatchSize = 3);
            h.Client.Log(TracerLevel.Info, "1");
            h.Client.Log(TracerLevel.Info, "2");
            Assert.That(h.Client.Stats.Queued, Is.EqualTo(0));
            h.Client.Log(TracerLevel.Info, "3");
            Assert.That(h.Client.Stats.Queued, Is.EqualTo(1));
            h.Client.Poll();
            Assert.That(h.Transport.Sent.Single().Envelope.Items[0].Header.GetNumber("item_count"), Is.EqualTo(3));
        }

        [Test]
        public void AttributesAreTypedAndOverflowIsMarked()
        {
            var h = new Harness();
            var big = new string('x', 6000);
            h.Client.Log(TracerLevel.Info, "a", Json.Object()
                .Set("n", 9007199254740992d)
                .Set("i", -3)
                .Set("o", Json.Object().Set("k", 1).Build())
                .SetNull("gone")
                .Set("b1", big).Set("b2", big).Set("b3", big)
                .Build());
            h.Client.FlushAsync();
            h.Client.Poll();
            var attrs = h.Transport.Sent.Single().Envelope.Items[0].Json.GetArrayOrEmpty("items")[0].GetMemberOrNull("attributes")!;
            Assert.That(attrs.GetMemberOrNull("n")!.GetString("type"), Is.EqualTo("double"), "past 2^53 a whole number is not an exact integer");
            Assert.That(attrs.GetMemberOrNull("i")!.GetString("type"), Is.EqualTo("integer"));
            Assert.That(attrs.GetMemberOrNull("o"), Is.EqualTo(Json.Object().Set("value", "{\"k\":1}").Set("type", "string").Build()));
            Assert.That(attrs.GetMemberOrNull("gone"), Is.Null);
            Assert.That(attrs.GetMemberOrNull("b1"), Is.Not.Null);
            Assert.That(attrs.GetMemberOrNull("b2"), Is.Not.Null);
            Assert.That(attrs.GetMemberOrNull("b3"), Is.Null, "past 16 KiB the rest does not fit");
            Assert.That(attrs.GetMemberOrNull("tracer.truncated"), Is.EqualTo(Json.Object().Set("value", true).Set("type", "boolean").Build()));
        }

        [Test]
        public void AttributeSizeCountsWhatGoEscapes()
        {
            var h = new Harness();
            // 3000 '<' are 3000 bytes here and 18000 after Go's HTML escaping.
            h.Client.Log(TracerLevel.Info, "a", Json.Object().Set("small", "x").Set("html", new string('<', 3000)).Build());
            h.Client.FlushAsync();
            h.Client.Poll();
            var attrs = h.Transport.Sent.Single().Envelope.Items[0].Json.GetArrayOrEmpty("items")[0].GetMemberOrNull("attributes")!;
            Assert.That(attrs.GetMemberOrNull("small"), Is.Not.Null);
            Assert.That(attrs.GetMemberOrNull("html"), Is.Null);
            Assert.That(attrs.GetMemberOrNull("tracer.truncated"), Is.Not.Null);
        }

        [Test]
        public void ACallerCannotOverrideTheCorrelationAttributes()
        {
            var h = new Harness(o => o.Tags = new Dictionary<string, string> { ["feature"] = "board" });
            h.Client.Log(TracerLevel.Info, "a", Json.Object()
                .Set("sentry.release", "forged").Set("user.id", "someone").Set("feature", "other").Set("own", 1).Build());
            h.Client.FlushAsync();
            h.Client.Poll();
            var attrs = h.Transport.Sent.Single().Envelope.Items[0].Json.GetArrayOrEmpty("items")[0].GetMemberOrNull("attributes")!;
            Assert.That(attrs.GetMemberOrNull("sentry.release")!.GetString("value"), Is.EqualTo("playground@0.1.0+1"));
            Assert.That(attrs.GetMemberOrNull("user.id")!.GetString("value"), Is.EqualTo("00000000000000000000000000000000"));
            Assert.That(attrs.GetMemberOrNull("feature")!.GetString("value"), Is.EqualTo("board"));
            Assert.That(attrs.GetMemberOrNull("own"), Is.Not.Null);
        }

        [Test]
        public void DataOverTheIngestLimitBecomesAMarker()
        {
            var h = new Harness();
            var big = Json.Object().Set("a", new string('x', 9000)).Set("b", new string('y', 9000)).Build();
            h.Client.Breadcrumb("c", "m", big, TracerLevel.Info);
            h.Client.Report("x", new TracerReportOptions { Data = big });
            h.Client.Poll();
            var e = h.Transport.Sent.Single().Envelope.Items[0].Json;
            var marker = Json.Object().Set("tracer.truncated", true).Build();
            Assert.That(e.GetMemberOrNull("extra"), Is.EqualTo(marker));
            Assert.That(e.GetMemberOrNull("breadcrumbs")!.GetArrayOrEmpty("values")[0].GetMemberOrNull("data"), Is.EqualTo(marker));
        }

        [Test]
        public void AnOversizedEventDropsItsOldestBreadcrumbs()
        {
            var h = new Harness();
            for (var i = 0; i < 500; i++)
            {
                h.Client.Breadcrumb("c", i + new string('x', 4000));
            }

            h.Client.Report("x");
            h.Client.Poll();
            var item = h.Transport.Sent.Single().Envelope.Items[0];
            Assert.That(item.Payload.Length, Is.LessThanOrEqualTo(TracerLimits.MaxJsonItemBytes));
            var crumbs = item.Json.GetMemberOrNull("breadcrumbs")!.GetArrayOrEmpty("values");
            Assert.That(crumbs.Count, Is.GreaterThan(0).And.LessThan(500));
            Assert.That(crumbs[crumbs.Count - 1].GetString("message"), Does.StartWith("499x"));
        }

        [Test]
        public void AttachmentsPastTheLimitAreDroppedAndCounted()
        {
            var h = new Harness();
            var files = Enumerable.Range(0, 21).Select(i => new TracerAttachment("f" + i, new byte[] { 1 }, "application/octet-stream", "event.attachment")).ToArray();
            h.Client.Report("x", new TracerReportOptions { Attachments = files });
            h.Client.Poll();
            Assert.That(h.Transport.Sent.Single().Envelope.Items.Count(i => i.Type == "attachment"), Is.EqualTo(20));
            Assert.That(Dropped(h.Client, "too_large"), Is.EqualTo(1));
        }

        [Test]
        public void AnExceptionBecomesTypeValueAndFrames()
        {
            var h = new Harness();
            Exception thrown;
            try
            {
                throw new ArgumentException("bad square");
            }
            catch (Exception error)
            {
                thrown = error;
            }

            h.Client.Report("x", new TracerReportOptions { Exception = thrown });
            h.Client.Poll();
            var value = h.Transport.Sent.Single().Envelope.Items[0].Json.GetMemberOrNull("exception")!.GetArrayOrEmpty("values")[0];
            Assert.That(value.GetString("type"), Is.EqualTo("System.ArgumentException"));
            Assert.That(value.GetString("value"), Is.EqualTo("bad square"));
            var frames = value.GetMemberOrNull("stacktrace")!.GetArrayOrEmpty("frames");
            Assert.That(frames.Count, Is.GreaterThan(0));
            Assert.That(frames[frames.Count - 1].GetString("function"), Does.Contain(nameof(AnExceptionBecomesTypeValueAndFrames)));
        }

        [Test]
        public void FeedbackIsItsOwnEnvelopeLinkedToTheReport()
        {
            var h = new Harness();
            h.Client.Breadcrumb("c", "m");
            var report = h.Client.Report("x")!;
            var dashed = Guid.ParseExact(report, "N").ToString("D").ToUpperInvariant();
            var id = h.Client.Feedback("the knight vanished", dashed);
            h.Client.Feedback("no link", "not-an-id");
            h.Client.Poll();
            Assert.That(h.Transport.Sent.Count, Is.EqualTo(3));
            var env = h.Transport.Sent[1].Envelope;
            Assert.That(env.Header.GetString("event_id"), Is.EqualTo(id));
            Assert.That(env.Items.Single().Type, Is.EqualTo("feedback"));
            var e = env.Items[0].Json;
            Assert.That(e.GetString("level"), Is.EqualTo("info"));
            Assert.That(e.GetMemberOrNull("breadcrumbs"), Is.Null);
            var feedback = e.GetMemberOrNull("contexts")!.GetMemberOrNull("feedback")!;
            Assert.That(feedback.GetString("message"), Is.EqualTo("the knight vanished"));
            Assert.That(feedback.GetString("associated_event_id"), Is.EqualTo(report));
            var unlinked = h.Transport.Sent[2].Envelope.Items[0].Json.GetMemberOrNull("contexts")!.GetMemberOrNull("feedback")!;
            Assert.That(unlinked.GetMemberOrNull("associated_event_id"), Is.Null);
        }

        [Test]
        public void TagsStopAt64AndANullValueRemovesOne()
        {
            var tags = Enumerable.Range(0, 70).ToDictionary(i => "t" + i, i => "v");
            var h = new Harness(o => o.Tags = tags);
            h.Client.SetTag("late", "v");
            h.Client.SetTag("t0", null);
            h.Client.SetTag("t1", "changed");
            h.Client.SetTag("late2", "v");
            h.Client.Report("x");
            h.Client.Poll();
            var sent = h.Transport.Sent.Single().Envelope.Items[0].Json.GetMemberOrNull("tags")!.AsObject();
            Assert.That(sent.Count, Is.EqualTo(64));
            Assert.That(sent.Any(t => t.Key == "t0"), Is.False);
            Assert.That(sent.Any(t => t.Key == "late"), Is.False, "the 65th key is ignored");
            Assert.That(sent.Single(t => t.Key == "t1").Value.AsString(), Is.EqualTo("changed"));
            Assert.That(sent.Any(t => t.Key == "late2"), Is.True, "a removal makes room");
        }

        [Test]
        public void A401StopsTheClientAndDropsWhatIsQueued()
        {
            var h = new Harness();
            h.Transport.Answer(401);
            h.Client.Report("a");
            h.Client.Report("b");
            h.Client.Log(TracerLevel.Info, "buffered");
            h.Client.Poll();
            Assert.That(h.Client.State, Is.EqualTo(TracerState.Stopped));
            Assert.That(h.Transport.Sent.Count, Is.EqualTo(1));
            Assert.That(Dropped(h.Client, "unauthorized"), Is.EqualTo(2), "the refused one and the queued one");
            Assert.That(Dropped(h.Client, "closed"), Is.EqualTo(1), "the buffered line");
            h.Client.Report("c");
            h.Advance(600_000);
            Assert.That(h.Transport.Sent.Count, Is.EqualTo(1));
            Assert.That(h.Log.Text, Does.Contain("tracer stopped: the ingest refused the key"));
        }

        [Test]
        public void TheKeyNeverReachesALogLine()
        {
            var h = new Harness();
            h.Transport.Answer(503).Throw().Answer(401);
            h.Client.Report("a");
            h.Client.Poll();
            h.Advance(10_000);
            h.Advance(10_000);
            // Positive control: the lines that name the resource were written.
            Assert.That(h.Log.Text, Does.Contain("tracer active"));
            Assert.That(h.Log.Text, Does.Contain("trc_fixture01"));
            Assert.That(h.Log.Text, Does.Contain("tracer send failed"));
            Assert.That(h.Log.Text, Does.Contain("tracer stopped"));
            Assert.That(h.Log.Text, Does.Not.Contain(Fixture.WriteKey));
            Assert.That(h.Log.Text, Does.Not.Contain("fixturewritekey"));
            Assert.That(h.Log.Text, Does.Not.Contain("sentry_key"));
        }

        [TestCase("10", 10_000)]
        [TestCase(null, 300_000)]
        [TestCase("0", 300_000)]
        [TestCase("-5", 300_000)]
        [TestCase("Wed, 21 Oct 2015 07:28:00 GMT", 300_000)]
        [TestCase("999999999", 86_400_000)]
        public void A429WaitsRetryAfterAndNeverLess(string? retryAfter, double waitMs)
        {
            var h = new Harness();
            h.Transport.Answer(429, retryAfter);
            h.Client.Report("a");
            h.Client.Poll();
            Assert.That(h.Transport.Sent.Count, Is.EqualTo(1));
            h.Advance(waitMs - 1);
            Assert.That(h.Transport.Sent.Count, Is.EqualTo(1));
            h.Advance(1);
            Assert.That(h.Transport.Sent.Count, Is.EqualTo(2));
            Assert.That(h.Client.Stats.EnvelopesSent, Is.EqualTo(1));
            Assert.That(h.Client.Stats.Dropped.Count, Is.EqualTo(0));
        }

        [Test]
        public void A503HonoursRetryAfter()
        {
            var h = new Harness();
            h.Transport.Answer(503, "60");
            h.Client.Report("a");
            h.Client.Poll();
            h.Advance(59_999);
            Assert.That(h.Transport.Sent.Count, Is.EqualTo(1));
            h.Advance(1);
            Assert.That(h.Transport.Sent.Count, Is.EqualTo(2));
        }

        [Test]
        public void FailuresWithoutRetryAfterBackOffWithJitterAndResetOnSuccess()
        {
            var h = new Harness(jitter: 0);
            h.Transport.Answer(503).Throw().Fault().Answer(200).Answer(502);
            h.Client.Report("a");
            h.Client.Report("b");
            h.Client.Poll();
            void Expect(double ms, int before, int after)
            {
                h.Advance(ms - 1);
                Assert.That(h.Transport.Sent.Count, Is.EqualTo(before), "nothing before " + ms);
                h.Advance(1);
                Assert.That(h.Transport.Sent.Count, Is.EqualTo(after), "resent at " + ms);
            }

            Expect(500, 1, 2);   // 1 s, jitter 0 → half
            Expect(1000, 2, 3);  // 2 s
            Expect(2000, 3, 5);  // 4 s; then 200 for "a", and "b" goes and gets a 502
            Expect(500, 5, 6);   // reset by the success
            Assert.That(h.Client.Stats.EnvelopesSent, Is.EqualTo(2));
        }

        [Test]
        public void TheBackOffStopsGrowingAtFiveMinutes()
        {
            var h = new Harness(jitter: 0.999999);
            for (var i = 0; i < 12; i++)
            {
                h.Transport.Answer(503);
            }

            h.Client.Report("a");
            h.Client.Poll();
            for (var i = 0; i < 11; i++)
            {
                h.Advance(300_000);
            }

            Assert.That(h.Transport.Sent.Count, Is.EqualTo(12));
        }

        [Test]
        public void ATransportThatThrowsSynchronouslyIsANetworkFailure()
        {
            var h = new Harness(o => o.Transport = new ThrowingTransport(), jitter: 0);
            h.Client.Report("a");
            h.Client.Poll();
            Assert.That(h.Client.State, Is.EqualTo(TracerState.Active));
            Assert.That(h.Client.Stats.Queued, Is.EqualTo(1));
        }

        [TestCase(400, "invalid")]
        [TestCase(413, "too_large")]
        [TestCase(418, "http_418")]
        public void ARefusalDropsTheEnvelopeAndTheNextOneGoes(int status, string reason)
        {
            var h = new Harness();
            h.Transport.Answer(status);
            h.Client.Report("a");
            h.Client.Report("b");
            h.Client.Poll();
            Assert.That(h.Transport.Sent.Count, Is.EqualTo(2));
            Assert.That(Dropped(h.Client, reason), Is.EqualTo(1));
            Assert.That(h.Client.Stats.EnvelopesSent, Is.EqualTo(1));
        }

        [Test]
        public void A415ResendsOnceWithoutGzipThenDrops()
        {
            var h = new Harness();
            h.Transport.Answer(415).Answer(415);
            h.Client.Report("a");
            h.Client.Report("b");
            h.Client.Poll();
            Assert.That(h.Transport.Sent.Count, Is.EqualTo(3));
            Assert.That(h.Transport.Sent[0].Header("Content-Encoding"), Is.EqualTo("gzip"));
            Assert.That(h.Transport.Sent[1].Header("Content-Encoding"), Is.Null);
            Assert.That(h.Transport.Sent[1].Body, Is.EqualTo(h.Transport.Sent[0].Body));
            Assert.That(h.Transport.Sent[2].Header("Content-Encoding"), Is.Null, "plain from then on");
            Assert.That(Dropped(h.Client, "encoding"), Is.EqualTo(1));
        }

        [Test]
        public void ARequestPastItsDeadlineIsAbandonedAndRetried()
        {
            var h = new Harness(o => o.Timeout = TimeSpan.FromSeconds(2), jitter: 0);
            h.Transport.Hang();
            h.Client.Report("a");
            h.Client.Poll();
            var token = h.Transport.LastToken;
            h.Advance(1999);
            Assert.That(token.IsCancellationRequested, Is.False);
            Assert.That(h.Client.Stats.Queued, Is.EqualTo(1));
            h.Advance(1);
            Assert.That(token.IsCancellationRequested, Is.True);
            h.Transport.Pending!.SetResult(new TracerHttpResponse(200, null));
            h.Advance(499);
            Assert.That(h.Transport.Sent.Count, Is.EqualTo(1));
            Assert.That(h.Client.Stats.EnvelopesSent, Is.EqualTo(0), "a late answer to an abandoned request counts for nothing");
            h.Advance(1);
            Assert.That(h.Transport.Sent.Count, Is.EqualTo(2));
            Assert.That(h.Client.Stats.EnvelopesSent, Is.EqualTo(1));
        }

        [Test]
        public void OneRequestIsInFlightAtATime()
        {
            var h = new Harness();
            h.Transport.Hang();
            h.Client.Report("a");
            h.Client.Report("b");
            h.Client.Poll();
            h.Client.Poll();
            Assert.That(h.Transport.Sent.Count, Is.EqualTo(1));
            Assert.That(h.Client.Stats.Queued, Is.EqualTo(2));
            h.Transport.Pending!.SetResult(new TracerHttpResponse(200, null));
            h.Client.Poll();
            Assert.That(h.Transport.Sent.Count, Is.EqualTo(2));
        }

        [Test]
        public void TheQueueDropsTheOldestPastItsBound()
        {
            var h = new Harness(o => o.MaxQueuedEnvelopes = 2, jitter: 0);
            h.Transport.Throw();
            h.Client.Report("a");
            h.Client.Poll();
            h.Client.Report("b");
            h.Client.Report("c");
            Assert.That(Dropped(h.Client, "queue_full"), Is.EqualTo(1));
            Assert.That(h.Client.Stats.Queued, Is.EqualTo(2));
            h.Advance(500);
            Assert.That(h.Transport.Sent.Skip(1).Select(s => s.Envelope.Items[0].Json.GetString("message")).ToArray(), Is.EqualTo(new[] { "b", "c" }));
        }

        [Test]
        public void TheByteBoundKeepsTheNewestEnvelope()
        {
            var h = new Harness(o => o.MaxQueuedBytes = 10, jitter: 0);
            h.Transport.Throw();
            h.Client.Report("a");
            h.Client.Poll();
            h.Client.Report("b");
            Assert.That(h.Client.Stats.Queued, Is.EqualTo(1));
            Assert.That(Dropped(h.Client, "queue_full"), Is.EqualTo(1));
        }

        [Test]
        public void FlushCompletesInPollOnceTheQueueIsEmpty()
        {
            var h = new Harness();
            h.Transport.Hang();
            h.Client.Log(TracerLevel.Info, "line");
            var flush = h.Client.FlushAsync();
            Assert.That(flush.IsCompleted, Is.False);
            Assert.That(h.Client.Stats.Queued, Is.EqualTo(1), "the buffered line was queued");
            h.Client.Poll();
            Assert.That(flush.IsCompleted, Is.False);
            h.Transport.Pending!.SetResult(new TracerHttpResponse(200, null));
            Assert.That(flush.IsCompleted, Is.False, "settled only inside Poll");
            h.Client.Poll();
            Assert.That(flush.IsCompleted, Is.True);
        }

        [Test]
        public void FlushDoesNotWaitOutABackOff()
        {
            var h = new Harness();
            h.Transport.Answer(503, "60");
            h.Client.Report("a");
            var flush = h.Client.FlushAsync();
            h.Client.Poll();
            Assert.That(flush.IsCompleted, Is.True);
            Assert.That(h.Client.FlushAsync().IsCompleted, Is.True);
        }

        [Test]
        public void CloseFlushesThenCloses()
        {
            var h = new Harness();
            h.Client.Log(TracerLevel.Info, "line");
            var close = h.Client.CloseAsync();
            Assert.That(close.IsCompleted, Is.False);
            Assert.That(h.Client.CloseAsync(), Is.SameAs(close));
            h.Client.Poll();
            Assert.That(close.IsCompleted, Is.True);
            Assert.That(h.Client.State, Is.EqualTo(TracerState.Closed));
            Assert.That(h.Transport.Sent.Count, Is.EqualTo(1));
            h.Client.Report("late");
            h.Advance(10_000);
            Assert.That(h.Transport.Sent.Count, Is.EqualTo(1));
        }

        [Test]
        public void CloseGivesUpAfterTheTimeoutAndCountsTheRest()
        {
            var h = new Harness(o => o.Timeout = TimeSpan.FromSeconds(2));
            h.Transport.Hang();
            h.Client.Report("a");
            h.Client.Report("b");
            h.Client.Poll();
            var close = h.Client.CloseAsync();
            h.Advance(1999);
            Assert.That(close.IsCompleted, Is.False);
            h.Advance(1);
            Assert.That(close.IsCompleted, Is.True);
            Assert.That(Dropped(h.Client, "closed"), Is.EqualTo(2));
        }

        [Test]
        public void DisposeStopsAtOnceAndSettlesWaiters()
        {
            var h = new Harness();
            h.Transport.Hang();
            h.Client.Report("a");
            h.Client.Poll();
            h.Client.Log(TracerLevel.Info, "buffered");
            h.Client.Report("b");
            var flush = h.Client.FlushAsync();
            h.Client.Dispose();
            Assert.That(flush.IsCompleted, Is.True);
            Assert.That(h.Client.State, Is.EqualTo(TracerState.Closed));
            Assert.That(h.Transport.LastToken.IsCancellationRequested, Is.True);
            Assert.That(Dropped(h.Client, "closed"), Is.EqualTo(2), "the one in flight and the report, which carried the buffered line");
            h.Client.Dispose();
            Assert.That(h.Client.CloseAsync().IsCompleted, Is.True);
        }

        [Test]
        public void TheLogWriterForwardsLinesAndBreadcrumbs()
        {
            var h = new Harness();
            var writer = TracerLogWriter.Create(h.Client, true);
            writer.Warn("slow frame", Json.Object().Set("ms", 40).Build());
            TracerLogWriter.Create(h.Client, false).Info("plain");
            h.Client.Report("x");
            h.Client.Poll();
            var env = h.Transport.Sent.Single().Envelope;
            var crumbs = env.Items[0].Json.GetMemberOrNull("breadcrumbs")!.GetArrayOrEmpty("values");
            Assert.That(crumbs.Single().GetString("category"), Is.EqualTo("log"));
            Assert.That(crumbs.Single().GetString("level"), Is.EqualTo("warning"));
            var lines = env.Items[1].Json.GetArrayOrEmpty("items");
            Assert.That(lines.Select(l => l.GetString("level")).ToArray(), Is.EqualTo(new[] { "warn", "info" }));
            Assert.That(lines[0].GetMemberOrNull("attributes")!.GetMemberOrNull("ms"), Is.EqualTo(Json.Object().Set("value", 40).Set("type", "integer").Build()));
        }

        [Test]
        public void WhatTheClientsOwnLoggerSendsBackIsIgnored()
        {
            ITracerClient? client = null;
            var log = new CapturingLogWriter();
            log.OnWrite = () =>
            {
                client?.Log(TracerLevel.Info, "echo");
                client?.Breadcrumb("echo", "echo");
                client?.Poll();
            };
            var transport = new FakeTransport();
            client = TracerClient.Create(new TracerOptions { Dsn = Fixture.Dsn, Transport = transport, Logger = log });
            client.Report("x");
            client.Poll();
            Assert.That(log.Lines.Count, Is.GreaterThan(1), "positive control: diagnostics were written");
            var env = transport.Sent.Single().Envelope;
            Assert.That(env.Items.Count, Is.EqualTo(1), "no echoed log line");
            Assert.That(env.Items[0].Json.GetMemberOrNull("breadcrumbs")!.GetArrayOrEmpty("values").Count, Is.EqualTo(0));
        }

        [Test]
        public void ABrokenLoggerIsNotTheGamesProblem()
        {
            var log = new CapturingLogWriter { OnWrite = () => throw new InvalidOperationException("writer") };
            var transport = new FakeTransport();
            var client = TracerClient.Create(new TracerOptions { Dsn = Fixture.Dsn, Transport = transport, Logger = log });
            Assert.That(client.Report("x"), Is.Not.Null);
            client.Poll();
            Assert.That(transport.Sent.Count, Is.EqualTo(1));
        }

        [Test]
        public void CallsFromSeveralThreadsLoseNothing()
        {
            var transport = new FakeTransport();
            var client = TracerClient.Create(new TracerOptions { Dsn = Fixture.Dsn, Transport = transport, LogBatchSize = 1000 });
            var threads = Enumerable.Range(0, 4).Select(t => new Thread(() =>
            {
                for (var i = 0; i < 250; i++)
                {
                    client.Trace("t", "m" + i);
                    client.Poll();
                }
            })).ToList();
            threads.ForEach(t => t.Start());
            threads.ForEach(t => t.Join());
            client.FlushAsync();
            for (var i = 0; i < 10; i++)
            {
                client.Poll();
            }

            var lines = transport.Sent.Sum(s => s.Envelope.Items.Where(x => x.Type == "log").Sum(x => (int)x.Header.GetNumber("item_count")!.Value));
            Assert.That(lines, Is.EqualTo(1000));
            Assert.That(client.Stats.Dropped.Count, Is.EqualTo(0));
        }

        [Test]
        public void OutOfRangeOptionsAreClampedNotThrown()
        {
            var h = new Harness(o =>
            {
                o.BreadcrumbCapacity = -1;
                o.LogBatchSize = 0;
                o.MaxQueuedEnvelopes = 0;
                o.Timeout = TimeSpan.FromSeconds(-1);
                o.FlushInterval = TimeSpan.Zero;
            });
            h.Client.Breadcrumb("c", "m");
            h.Client.Log(TracerLevel.Info, "a");
            Assert.That(h.Client.Stats.Queued, Is.EqualTo(1), "a batch of one is queued at once");
            h.Client.Report("x");
            Assert.That(Dropped(h.Client, "queue_full"), Is.EqualTo(1), "a queue of one keeps the newest");
            h.Client.Poll();
            Assert.That(h.Transport.Sent.Count, Is.EqualTo(1));
            Assert.That(h.Transport.Sent[0].Envelope.Items[0].Json.GetMemberOrNull("breadcrumbs")!.GetArrayOrEmpty("values").Count, Is.EqualTo(0));
            Assert.That(h.Transport.Sent[0].Request.Timeout, Is.EqualTo(TimeSpan.FromSeconds(30)));
        }

        [Test]
        public void ContextsKeepTheGamesButNotTraceOrFeedback()
        {
            var h = new Harness(o => o.Contexts = Json.Object()
                .Set("app", Json.Object().Set("build", "7").Build())
                .Set("trace", Json.Object().Set("trace_id", "x").Build())
                .Set("feedback", Json.Object().Set("message", "x").Build())
                .Build());
            h.Client.Report("x");
            h.Client.Poll();
            var contexts = h.Transport.Sent.Single().Envelope.Items[0].Json.GetMemberOrNull("contexts")!;
            Assert.That(contexts.AsObject().Select(c => c.Key).ToArray(), Is.EqualTo(new[] { "app", "trace" }));
            Assert.That(contexts.GetMemberOrNull("trace")!.GetString("trace_id"), Is.EqualTo(h.Client.TraceId));
        }

        private sealed class ThrowingTransport : ITracerTransport
        {
            public Task<TracerHttpResponse> SendAsync(TracerHttpRequest request, CancellationToken cancellationToken)
                => throw new InvalidOperationException("synchronous");
        }
    }
}
