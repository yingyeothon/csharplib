using System;
using System.Collections.Generic;
using NUnit.Framework;
using Yingyeothon.Codec;

namespace Yingyeothon.Logger.Tests
{
    [TestFixture]
    public class LoggerTests
    {
        private static (ILogger Logger, CapturingLogWriter Writer) Setup(LogSeverity severity)
        {
            var writer = new CapturingLogWriter();
            var logger = FilteredLogger.Create(new FilteredLoggerOptions { Severity = severity, Writer = writer });
            return (logger, writer);
        }

        private static void WriteAll(ILogWriter writer)
        {
            writer.Debug("d");
            writer.Info("i");
            writer.Warn("w");
            writer.Error("e");
        }

        [Test]
        public void InfoHidesDebugAndKeepsTheRest()
        {
            var (logger, writer) = Setup(LogSeverity.Info);

            WriteAll(logger);

            Assert.That(writer.Lines, Has.Count.EqualTo(3));
            Assert.That(writer.Text, Does.Not.Contain("[DEBUG]"));
            Assert.That(writer.Text, Does.Contain("[INFO]"));
        }

        [Test]
        public void DebugLetsEverythingThrough()
        {
            var (logger, writer) = Setup(LogSeverity.Debug);

            WriteAll(logger);

            Assert.That(writer.Lines, Has.Count.EqualTo(4));
        }

        [Test]
        public void ErrorHidesWarn()
        {
            var (logger, writer) = Setup(LogSeverity.Error);

            WriteAll(logger);

            Assert.That(writer.Lines, Has.Count.EqualTo(1));
            Assert.That(writer.Text, Does.Contain("[ERROR]"));
        }

        [Test]
        public void NoneSuppressesEverything()
        {
            var (logger, writer) = Setup(LogSeverity.None);

            WriteAll(logger);

            Assert.That(writer.Lines, Is.Empty);
        }

        [Test]
        public void SeverityIsReadOnEveryCallSoItCanBeChangedAtRuntime()
        {
            var (logger, writer) = Setup(LogSeverity.Info);

            logger.Debug("hidden");
            logger.Severity = LogSeverity.Debug;
            logger.Debug("shown");

            Assert.That(writer.Text, Does.Not.Contain("hidden"));
            Assert.That(writer.Text, Does.Contain("shown"));
        }

        [Test]
        public void IsEnabledMatchesWhatIsActuallyWritten()
        {
            var (logger, _) = Setup(LogSeverity.Warn);

            Assert.That(logger.IsEnabled(LogSeverity.Debug), Is.False);
            Assert.That(logger.IsEnabled(LogSeverity.Info), Is.False);
            Assert.That(logger.IsEnabled(LogSeverity.Warn), Is.True);
            Assert.That(logger.IsEnabled(LogSeverity.Error), Is.True);
        }

        [Test]
        public void TheContextIsRenderedAfterTheMessage()
        {
            var (logger, writer) = Setup(LogSeverity.Debug);

            logger.Info("lobby connected", Json.Object().Set("userId", "alice").Set("tick", 200d).Build());

            Assert.That(writer.Lines[0], Is.EqualTo("[INFO] lobby connected {\"userId\":\"alice\",\"tick\":200}"));
        }

        [Test]
        public void AMessageWithoutContextRendersAlone()
        {
            var (logger, writer) = Setup(LogSeverity.Debug);

            logger.Warn("something");

            Assert.That(writer.Lines[0], Is.EqualTo("[WARN] something"));
        }

        [Test]
        public void NullLoggerWritesNothingAndReportsNone()
        {
            Assert.That(NullLogger.Instance.Severity, Is.EqualTo(LogSeverity.None));
            Assert.That(NullLogger.Instance.IsEnabled(LogSeverity.Error), Is.False);

            NullLogger.Instance.Severity = LogSeverity.Debug;

            Assert.That(NullLogger.Instance.Severity, Is.EqualTo(LogSeverity.None));
            Assert.DoesNotThrow(() => WriteAll(NullLogger.Instance));
        }

        [Test]
        public void CreateRefusesOptionsWithoutAWriter()
        {
            Assert.Throws<ArgumentNullException>(() => FilteredLogger.Create(null!));
            Assert.Throws<ArgumentException>(() => FilteredLogger.Create(new FilteredLoggerOptions()));
        }

        [Test]
        public void ConsoleLoggerDefaultsToInfo()
        {
            Assert.That(ConsoleLogger.Create().Severity, Is.EqualTo(LogSeverity.Info));
            Assert.That(ConsoleLogger.Create(LogSeverity.Warn).Severity, Is.EqualTo(LogSeverity.Warn));
        }
    }

    [TestFixture]
    public class LogWritersTests
    {
        [Test]
        public void CombineFansOutToEveryWriterInOrder()
        {
            var order = new List<string>();
            var first = LogWriters.FromAction((s, m, c) => order.Add("first:" + m));
            var second = LogWriters.FromAction((s, m, c) => order.Add("second:" + m));

            LogWriters.Combine(first, second).Info("hello");

            Assert.That(order, Is.EqualTo(new[] { "first:hello", "second:hello" }));
        }

        [Test]
        public void CombineSkipsExactlyTheNullLoggerSingleton()
        {
            var captured = new CapturingLogWriter();

            // A different writer that discards everything is still called; only the
            // shared NullLogger instance is recognised and dropped.
            LogWriters.Combine(NullLogger.Instance, captured, LogWriters.Null).Error("boom");

            Assert.That(captured.Lines, Has.Count.EqualTo(1));
            Assert.DoesNotThrow(() => LogWriters.Combine(NullLogger.Instance).Error("boom"));
        }

        [Test]
        public void CombineRefusesANullEntry()
        {
            Assert.Throws<ArgumentNullException>(() => LogWriters.Combine(null!));
            Assert.Throws<ArgumentException>(() => LogWriters.Combine(new ILogWriter[] { null! }));
        }

        [Test]
        public void FromActionForwardsTheSeverityMessageAndContext()
        {
            var seen = new List<string>();
            var writer = LogWriters.FromAction((s, m, c) => seen.Add(s + "|" + m + "|" + (c == null ? "-" : Json.Stringify(c))));

            writer.Debug("d");
            writer.Info("i", Json.Object().Set("k", "v").Build());
            writer.Warn("w");
            writer.Error("e");

            Assert.That(seen, Is.EqualTo(new[]
            {
                "Debug|d|-",
                "Info|i|{\"k\":\"v\"}",
                "Warn|w|-",
                "Error|e|-",
            }));
        }

        [Test]
        public void FromActionRefusesANullSink()
        {
            Assert.Throws<ArgumentNullException>(() => LogWriters.FromAction(null!));
        }

        [Test]
        public void TheNullWriterDiscardsEverything()
        {
            Assert.DoesNotThrow(() =>
            {
                LogWriters.Null.Debug("d");
                LogWriters.Null.Error("e", Json.Object().Build());
            });
        }
    }
}
