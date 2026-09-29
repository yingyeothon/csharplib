using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Yingyeothon.Codec;
using Yingyeothon.Logger;

namespace Yingyeothon.Assets.Tests
{
    [TestFixture]
    public class AssetClientTests
    {
        private const string Base = "https://cdn.test/assets/bnd_1/";
        private static readonly (byte[] Bytes, string Text) Key = TestEncryptor.Key(3);

        private static string Url(string path) => Base + path;

        private static IAssetBundleClient Client(FakeCdn cdn, bool encrypted = true, bool corsSafe = false, ILogger? logger = null, string baseUrl = Base)
            => AssetBundleClient.Create(new AssetBundleClientOptions
            {
                BaseUrl = baseUrl,
                Key = encrypted ? Key.Text : null,
                Transport = cdn,
                CorsSafe = corsSafe,
                Logger = logger,
            });

        private static byte[] Put(FakeCdn cdn, string path, byte[] plaintext, string adPrefix = "")
        {
            cdn.Put(Url(adPrefix + path), TestEncryptor.Encrypt(Key.Bytes, adPrefix + path, plaintext));
            return plaintext;
        }

        private static ILogger Capture(CapturingLogWriter log)
            => FilteredLogger.Create(new FilteredLoggerOptions { Severity = LogSeverity.Debug, Writer = log });

        // ---- answers the CDN gives ------------------------------------------------

        [TestCase(403)]
        [TestCase(404)]
        public void AMissingObjectIsNotFound(int status)
        {
            var cdn = new FakeCdn { StatusOverride = _ => status };

            var error = Assert.ThrowsAsync<AssetClientException>(() => Client(cdn).ReadAsync("data/missing.db"));

            Assert.That(error!.Code, Is.EqualTo(AssetErrorCodes.NotFound));
            Assert.That(error.Status, Is.EqualTo(status));
            Assert.That(error.Message, Is.EqualTo("asset not_found (" + status + ")"));
            Assert.That(cdn.OpenBodies, Is.EqualTo(0));
        }

        [Test]
        public void AnyOtherStatusIsHttp()
        {
            var cdn = new FakeCdn { StatusOverride = _ => 500 };

            var error = Assert.ThrowsAsync<AssetClientException>(() => Client(cdn).ReadAsync("a.bin"));

            Assert.That(error!.Code, Is.EqualTo(AssetErrorCodes.Http));
            Assert.That(error.Status, Is.EqualTo(500));
        }

        [Test]
        public void ATransportThatThrowsIsNetworkAndLogsOnlyTheKindAndPath()
        {
            var log = new CapturingLogWriter();
            var cdn = new FakeCdn { RefuseRange = true };
            Put(cdn, "a.bin", TestEncryptor.Pattern(10));

            var error = Assert.ThrowsAsync<AssetClientException>(() => Client(cdn, logger: Capture(log)).ReadRangeAsync("a.bin", 0, 5));

            Assert.That(error!.Code, Is.EqualTo(AssetErrorCodes.Network));
            Assert.That(error.InnerException, Is.InstanceOf<IOException>());
            Assert.That(log.Text, Does.Contain("asset request failed"));
            Assert.That(log.Text, Does.Contain("\"path\":\"a.bin\""));
            Assert.That(log.Text, Does.Not.Contain("cdn.test"));
        }

        // ---- the key --------------------------------------------------------------

        [TestCase("yak2.ASNFZ4mrze8BI0VniavN7wEjRWeJq83vASNFZ4mrze8")]
        [TestCase("yak1.ASNFZ4mrze8BI0VniavN7wEjRWeJq83vASNFZ4mrze")]
        [TestCase("yak1.ASNFZ4mrze8BI0VniavN7wEjRWeJq83vASNFZ4mrze8=")]
        [TestCase("yak1.ASNFZ4mrze8BI0VniavN7wEjRWeJq83vASNFZ4mr+e8")]
        [TestCase("yak1.ASNFZ4mrze8BI0VniavN7wEjRWeJq83vASNFZ4mr/e8")]
        [TestCase("")]
        public void ANonCanonicalKeyIsBadKeyAndIsNeverQuoted(string key)
        {
            var error = Assert.Throws<AssetClientException>(() => AssetBundleClient.Create(new AssetBundleClientOptions { BaseUrl = Base, Key = key }));

            Assert.That(error!.Code, Is.EqualTo(AssetErrorCodes.BadKey));
            Assert.That(error.Message, Is.EqualTo("asset bad_key (0)"));
        }

        [Test]
        public void ALastCharacterOutsideTheCanonicalSixteenIsBadKey()
        {
            // Built in two pieces so the source holds no 43-character key the secret
            // scanner would flag; `9` decodes to the same bytes as the canonical `8`.
            var candidate = "yak1.ASNFZ4mrze8BI0VniavN7wEjRWeJq83vASNFZ4mrze" + "9";

            var error = Assert.Throws<AssetClientException>(() => AssetBundleClient.Create(new AssetBundleClientOptions { BaseUrl = Base, Key = candidate }));

            Assert.That(error!.Code, Is.EqualTo(AssetErrorCodes.BadKey));
        }

        [Test]
        public void KeyBytesMustBe32AndNotBothForms()
        {
            Assert.Throws<AssetClientException>(() => AssetBundleClient.Create(new AssetBundleClientOptions { BaseUrl = Base, KeyBytes = new byte[31] }));
            Assert.Throws<AssetClientException>(() => AssetBundleClient.Create(new AssetBundleClientOptions { BaseUrl = Base, KeyBytes = Key.Bytes, Key = Key.Text }));
            Assert.That(AssetBundleClient.Create(new AssetBundleClientOptions { BaseUrl = Base, KeyBytes = Key.Bytes }), Is.Not.Null);
        }

        [Test]
        public void TheCallersKeyBytesAreCopied()
        {
            var cdn = new FakeCdn();
            var plain = Put(cdn, "a.bin", TestEncryptor.Pattern(100));
            var bytes = (byte[])Key.Bytes.Clone();
            var client = AssetBundleClient.Create(new AssetBundleClientOptions { BaseUrl = Base, KeyBytes = bytes, Transport = cdn });

            Array.Clear(bytes, 0, bytes.Length);

            Assert.That(client.ReadAsync("a.bin").Result, Is.EqualTo(plain));
        }

        // ---- base URL and paths ---------------------------------------------------

        [TestCase("")]
        [TestCase("cdn.test/assets/b/")]
        [TestCase("ftp://cdn.test/assets/b/")]
        [TestCase("https://u:p@cdn.test/assets/b/")]
        [TestCase("https://cdn.test/assets/b/?x=1")]
        [TestCase("https://cdn.test/assets/b/#f")]
        [TestCase("https://cdn.test/other/b/")]
        [TestCase("https://cdn.test/assets/b/v1/deeper/")]
        public void AKeyedBaseUrlMustBeABundleOrAVersion(string baseUrl)
        {
            Assert.Throws<ArgumentException>(() => AssetBundleClient.Create(new AssetBundleClientOptions { BaseUrl = baseUrl, Key = Key.Text }));
        }

        [Test]
        public void APlainBaseUrlMayBeAnyHttpPath()
        {
            Assert.That(AssetBundleClient.Create(new AssetBundleClientOptions { BaseUrl = "https://cdn.test/other/place" }), Is.Not.Null);
        }

        [Test]
        public void AVersionedBundleBindsTheVersionIntoTheAssociatedData()
        {
            var cdn = new FakeCdn();
            var plain = Put(cdn, "data.bin", TestEncryptor.Pattern(70000), adPrefix: "v3/");

            var client = Client(cdn, baseUrl: Base + "v3");

            Assert.That(client.ReadAsync("data.bin").Result, Is.EqualTo(plain));
        }

        [Test]
        public void AFolderMistakenForABaseUrlFailsAsCorruptLikeAWrongKey()
        {
            // …/bnd_1/music/ has the shape of version `music`; the bytes were bound to the
            // live path `music/intro.ogg`, not `music/` + `intro.ogg` as a version.
            var cdn = new FakeCdn();
            cdn.Put(Base + "music/intro.ogg", TestEncryptor.Encrypt(Key.Bytes, "intro.ogg", TestEncryptor.Pattern(10)));

            var error = Assert.ThrowsAsync<AssetClientException>(() => Client(cdn, baseUrl: Base + "music/").ReadAsync("intro.ogg"));

            Assert.That(error!.Code, Is.EqualTo(AssetErrorCodes.AssetCorrupt));
        }

        [TestCase("")]
        [TestCase("/a.bin")]
        [TestCase("a//b.bin")]
        [TestCase("a/./b.bin")]
        [TestCase("a/../b.bin")]
        [TestCase("a\\b.bin")]
        [TestCase("a\nb.bin")]
        public void AMalformedPathIsRefusedBeforeAnyRequest(string path)
        {
            var cdn = new FakeCdn();

            Assert.ThrowsAsync<ArgumentException>(() => Client(cdn).ReadAsync(path));
            Assert.That(cdn.Requests, Is.Empty);
        }

        [Test]
        public void ANonAsciiPathIsEscapedInTheUrlAndBoundRawInTheData()
        {
            var cdn = new FakeCdn();
            var plain = TestEncryptor.Pattern(20);
            cdn.Put(Base + Uri.EscapeDataString("데이터") + "/" + Uri.EscapeDataString("노래.db"), TestEncryptor.Encrypt(Key.Bytes, "데이터/노래.db", plain));

            Assert.That(Client(cdn).ReadAsync("데이터/노래.db").Result, Is.EqualTo(plain));
        }

        // ---- what goes on the wire ------------------------------------------------

        [Test]
        public void ARangeInTheFirstSegmentIsOneRangedGet()
        {
            var cdn = new FakeCdn();
            var plain = Put(cdn, "a.bin", TestEncryptor.Pattern(200000));

            var bytes = Client(cdn).ReadRangeAsync("a.bin", 10, 20).Result;

            Assert.That(bytes, Is.EqualTo(plain.AsSpan(10, 10).ToArray()));
            Assert.That(cdn.Requests.Count, Is.EqualTo(1));
            Assert.That(cdn.Requests[0].Header("Range"), Is.EqualTo("bytes=0-65535"));
            Assert.That(cdn.OpenBodies, Is.EqualTo(0));
        }

        [Test]
        public void ARangeAfterItIsTheHeaderThenTheSegmentsUnderIfRange()
        {
            var cdn = new FakeCdn();
            var plain = Put(cdn, "a.bin", TestEncryptor.Pattern(200000));

            var bytes = Client(cdn).ReadRangeAsync("a.bin", 70000, 70010).Result;

            Assert.That(bytes, Is.EqualTo(plain.AsSpan(70000, 10).ToArray()));
            Assert.That(cdn.Requests.Select(r => r.Header("Range")), Is.EqualTo(new[] { "bytes=0-39", "bytes=65536-131071" }));
            Assert.That(cdn.Requests[1].Header("If-Range"), Is.EqualTo("\"etag-1\""));
        }

        [Test]
        public void CorsSafeModeLearnsTheLengthFromAHeadAndSendsOnlyRange()
        {
            var cdn = new FakeCdn { CrossOrigin = true };
            var plain = Put(cdn, "a.bin", TestEncryptor.Pattern(200000));

            var bytes = Client(cdn, corsSafe: true).ReadRangeAsync("a.bin", 70000, 70010, new AssetReadOptions { NoCache = true }).Result;

            Assert.That(bytes, Is.EqualTo(plain.AsSpan(70000, 10).ToArray()));
            Assert.That(cdn.Requests.Select(r => r.Method), Is.EqualTo(new[] { "HEAD", "GET", "GET" }));
            foreach (var request in cdn.Requests)
            {
                Assert.That(request.Headers.Select(h => h.Key).Where(k => k != "Range"), Is.Empty);
            }
        }

        [Test]
        public void NoCacheIsSentOutsideCorsSafeMode()
        {
            var cdn = new FakeCdn();
            Put(cdn, "a.bin", TestEncryptor.Pattern(10));

            Client(cdn).ReadAsync("a.bin", new AssetReadOptions { NoCache = true }).Wait();

            Assert.That(cdn.Requests[0].Header("Cache-Control"), Is.EqualTo("no-cache"));
        }

        [Test]
        public void ABrowserThatStillPreflightsRangeFallsBackToTheWholeFile()
        {
            var log = new CapturingLogWriter();
            var cdn = new FakeCdn { CrossOrigin = true, RefuseRange = true };
            var plain = Put(cdn, "a.bin", TestEncryptor.Pattern(200000));

            var bytes = Client(cdn, corsSafe: true, logger: Capture(log)).ReadRangeAsync("a.bin", 140000, 140100).Result;

            Assert.That(bytes, Is.EqualTo(plain.AsSpan(140000, 100).ToArray()));
            Assert.That(log.Text, Does.Contain("asset ranged request refused; reading the whole file"));
            Assert.That(cdn.OpenBodies, Is.EqualTo(0));
        }

        // ---- the object changing under a read ------------------------------------

        [Test]
        public void AChangeBetweenTheHeaderAndTheSegmentsStartsOver()
        {
            var log = new CapturingLogWriter();
            var cdn = new FakeCdn();
            Put(cdn, "a.bin", TestEncryptor.Pattern(200000, 1));
            var second = TestEncryptor.Pattern(200000, 2);
            cdn.BeforeAnswer = (index, _) =>
            {
                if (index == 1)
                {
                    Put(cdn, "a.bin", second);
                }
            };

            var bytes = Client(cdn, logger: Capture(log)).ReadRangeAsync("a.bin", 70000, 70010).Result;

            Assert.That(bytes, Is.EqualTo(second.AsSpan(70000, 10).ToArray()));
            Assert.That(log.Text, Does.Contain("asset changed during a read; starting over"));
            Assert.That(cdn.OpenBodies, Is.EqualTo(0));
        }

        [Test]
        public void AnObjectThatKeepsChangingIsHttpAfterThreeRestarts()
        {
            var cdn = new FakeCdn();
            Put(cdn, "a.bin", TestEncryptor.Pattern(200000, 1));
            var seed = 1;
            cdn.BeforeAnswer = (index, _) =>
            {
                if (index % 2 == 1)
                {
                    Put(cdn, "a.bin", TestEncryptor.Pattern(200000, ++seed));
                }
            };

            var error = Assert.ThrowsAsync<AssetClientException>(() => Client(cdn).ReadRangeAsync("a.bin", 70000, 70010));

            Assert.That(error!.Code, Is.EqualTo(AssetErrorCodes.Http));
            Assert.That(error.Detail, Is.EqualTo("the object kept changing"));
            Assert.That(cdn.OpenBodies, Is.EqualTo(0));
        }

        [Test]
        public void AHostThatIgnoresRangeIsHttpForAnEncryptedRange()
        {
            var cdn = new FakeCdn { IgnoreRange = true };
            Put(cdn, "a.bin", TestEncryptor.Pattern(200000));

            var error = Assert.ThrowsAsync<AssetClientException>(() => Client(cdn).ReadRangeAsync("a.bin", 70000, 70010));

            Assert.That(error!.Code, Is.EqualTo(AssetErrorCodes.Http));
            Assert.That(error.Detail, Is.EqualTo("the host ignores Range"));
        }

        [Test]
        public void ARangedAnswerWithoutAContentRangeIsHttpOutsideCorsSafeMode()
        {
            var cdn = new FakeCdn { CrossOrigin = true };
            Put(cdn, "a.bin", TestEncryptor.Pattern(10));

            var error = Assert.ThrowsAsync<AssetClientException>(() => Client(cdn).ReadRangeAsync("a.bin", 0, 5));

            Assert.That(error!.Code, Is.EqualTo(AssetErrorCodes.Http));
            Assert.That(error.Detail, Is.EqualTo("no Content-Range"));
        }

        // ---- plain bundles --------------------------------------------------------

        [Test]
        public void APlainBundleReadsWholeAndByRange()
        {
            var cdn = new FakeCdn();
            var plain = TestEncryptor.Pattern(1000);
            cdn.Put(Url("a.bin"), plain);
            var client = Client(cdn, encrypted: false);

            Assert.That(client.ReadAsync("a.bin").Result, Is.EqualTo(plain));
            Assert.That(client.ReadRangeAsync("a.bin", 100, 200).Result, Is.EqualTo(plain.AsSpan(100, 100).ToArray()));
            Assert.That(client.ReadRangeAsync("a.bin", 5000, null).Result, Is.Empty);
            Assert.That(cdn.OpenBodies, Is.EqualTo(0));
        }

        [Test]
        public void AnEmptyWindowMakesNoRequest()
        {
            var cdn = new FakeCdn();

            Assert.That(Client(cdn).ReadRangeAsync("a.bin", 10, 10).Result, Is.Empty);
            Assert.That(cdn.Requests, Is.Empty);
        }

        [Test]
        public void NegativeOffsetsAreRefused()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Client(new FakeCdn()).ReadRangeAsync("a.bin", -1, null));
        }

        // ---- JSON -----------------------------------------------------------------

        [Test]
        public void ReadJsonDecodesAManifestAndDropsABom()
        {
            var cdn = new FakeCdn();
            Put(cdn, "manifest.json", Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("{\"db\":\"data/songs-3f9a.db\"}")).ToArray());

            var manifest = Client(cdn).ReadJsonAsync("manifest.json").Result;

            Assert.That(manifest.GetString("db"), Is.EqualTo("data/songs-3f9a.db"));
        }

        [Test]
        public void ReadJsonOnBytesThatAreNotUtf8JsonIsCorrupt()
        {
            var cdn = new FakeCdn();
            Put(cdn, "bad.json", new byte[] { 0xff, 0xfe, 0x7b });
            Put(cdn, "text.json", Encoding.UTF8.GetBytes("not json at all"));

            foreach (var path in new[] { "bad.json", "text.json" })
            {
                var error = Assert.ThrowsAsync<AssetClientException>(() => Client(cdn).ReadJsonAsync(path));
                Assert.That(error!.Code, Is.EqualTo(AssetErrorCodes.AssetCorrupt));
            }
        }

        [Test]
        public void OmittingTheKeyOfAnEncryptedBundleReadsCiphertextAndOnlyJsonNotices()
        {
            var cdn = new FakeCdn();
            Put(cdn, "manifest.json", Encoding.UTF8.GetBytes("{}"));
            var plainClient = Client(cdn, encrypted: false);

            Assert.That(plainClient.ReadAsync("manifest.json").Result[0], Is.EqualTo(0x28));
            var error = Assert.ThrowsAsync<AssetClientException>(() => plainClient.ReadJsonAsync("manifest.json"));
            Assert.That(error!.Code, Is.EqualTo(AssetErrorCodes.AssetCorrupt));
        }

        // ---- downloads ------------------------------------------------------------

        [Test]
        public void ADownloadWritesVerifiedSegmentsAndReportsProgress()
        {
            var cdn = new FakeCdn();
            var plain = Put(cdn, "a.bin", TestEncryptor.Pattern(200000));
            var sink = new MemorySink();
            var reports = 0;

            var result = Client(cdn).DownloadAsync("a.bin", sink, new AssetDownloadOptions { OnProgress = p => reports++ }).Result;

            Assert.That(sink.Bytes, Is.EqualTo(plain));
            Assert.That(result.Bytes, Is.EqualTo(200000L));
            Assert.That(result.ETag, Is.EqualTo("\"etag-1\""));
            Assert.That(sink.Events, Is.EqualTo(new[] { "write:65464", "write:65504", "write:65504", "write:3528" }));
            Assert.That(reports, Is.EqualTo(4));
            Assert.That(cdn.OpenBodies, Is.EqualTo(0));
        }

        [Test]
        public void AResumeFetchesAtMostOneSegmentAgain()
        {
            var cdn = new FakeCdn();
            var plain = Put(cdn, "a.bin", TestEncryptor.Pattern(200000));
            var sink = new MemorySink();
            sink.Seed(plain.AsSpan(0, 100000).ToArray());

            Client(cdn).DownloadAsync("a.bin", sink, new AssetDownloadOptions { Resume = new AssetResume(100000, "\"etag-1\"") }).Wait();

            Assert.That(sink.Bytes, Is.EqualTo(plain));
            Assert.That(cdn.Requests.Select(r => r.Header("Range")), Is.EqualTo(new[] { "bytes=0-39", "bytes=65536-" + (200000 + 40 + 32 * 4 - 1) }));
        }

        [Test]
        public void AResumeAgainstAnotherObjectResetsTheSinkAndStartsOver()
        {
            var cdn = new FakeCdn();
            var plain = Put(cdn, "a.bin", TestEncryptor.Pattern(200000));
            var sink = new MemorySink();
            sink.Seed(new byte[100000]);

            Client(cdn).DownloadAsync("a.bin", sink, new AssetDownloadOptions { Resume = new AssetResume(100000, "\"etag-old\"") }).Wait();

            Assert.That(sink.Events[0], Is.EqualTo("reset"));
            Assert.That(sink.Bytes, Is.EqualTo(plain));
        }

        [Test]
        public void AKeyedResumePastTheEndIsTheCallersMistakeOnceTheLastSegmentVerified()
        {
            var cdn = new FakeCdn();
            var ciphertext = TestEncryptor.Encrypt(Key.Bytes, "a.bin", TestEncryptor.Pattern(100));
            cdn.Put(Url("a.bin"), ciphertext);

            Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => Client(cdn).DownloadAsync("a.bin", new MemorySink(), new AssetDownloadOptions { Resume = new AssetResume(500, "\"etag-1\"") }));

            // Only once it verified: the same resume against a tampered last segment is the
            // file's fault, not the caller's.
            ciphertext[ciphertext.Length - 1] ^= 1;
            var etag = cdn.Put(Url("a.bin"), ciphertext);
            var error = Assert.ThrowsAsync<AssetClientException>(
                () => Client(cdn).DownloadAsync("a.bin", new MemorySink(), new AssetDownloadOptions { Resume = new AssetResume(500, etag) }));
            Assert.That(error!.Code, Is.EqualTo(AssetErrorCodes.AssetCorrupt));
        }

        [Test]
        public void WhatTheSinkThrowsReachesTheCallerUnchanged()
        {
            var cdn = new FakeCdn();
            Put(cdn, "a.bin", TestEncryptor.Pattern(100));

            var error = Assert.ThrowsAsync<InvalidOperationException>(
                () => Client(cdn).DownloadAsync("a.bin", new MemorySink(), new AssetDownloadOptions { OnProgress = _ => throw new InvalidOperationException("stop") }));

            Assert.That(error!.Message, Is.EqualTo("stop"));
            Assert.That(cdn.OpenBodies, Is.EqualTo(0));
        }

        [Test]
        public void APlainDownloadResumesUnderIfRange()
        {
            var cdn = new FakeCdn();
            var plain = TestEncryptor.Pattern(50000);
            var etag = cdn.Put(Url("a.bin"), plain);
            var sink = new MemorySink();
            sink.Seed(plain.AsSpan(0, 20000).ToArray());

            var result = Client(cdn, encrypted: false).DownloadAsync("a.bin", sink, new AssetDownloadOptions { Resume = new AssetResume(20000, etag) }).Result;

            Assert.That(sink.Bytes, Is.EqualTo(plain));
            Assert.That(result.Bytes, Is.EqualTo(50000L));
            Assert.That(cdn.Requests[0].Header("If-Range"), Is.EqualTo(etag));
        }

        [Test]
        public void DownloadToFileWritesThePartThenTheFileAndResumesFromIt()
        {
            var folder = Path.Combine(Path.GetTempPath(), "yyt-asset-" + Guid.NewGuid().ToString("N"));
            try
            {
                var destination = Path.Combine(folder, "sub", "a.bin");
                var cdn = new FakeCdn();
                var plain = Put(cdn, "a.bin", TestEncryptor.Pattern(200000));
                var client = Client(cdn);

                // An interrupted first attempt: the progress callback throws after the first piece.
                Assert.ThrowsAsync<InvalidOperationException>(
                    () => AssetFiles.DownloadToFileAsync(client, "a.bin", destination, _ => throw new InvalidOperationException("stop")));
                Assert.That(File.Exists(destination), Is.False);
                Assert.That(File.Exists(destination + ".part"), Is.True);
                Assert.That(File.ReadAllText(destination + ".part.etag"), Is.EqualTo("\"etag-1\""));

                cdn.Requests.Clear();
                var result = AssetFiles.DownloadToFileAsync(client, "a.bin", destination).Result;

                Assert.That(File.ReadAllBytes(destination), Is.EqualTo(plain));
                Assert.That(result.Bytes, Is.EqualTo(200000L));
                Assert.That(File.Exists(destination + ".part"), Is.False);
                Assert.That(File.Exists(destination + ".part.etag"), Is.False);
                // The part held segment 0 whole, so the resume starts at segment 1: the header,
                // then everything from 65,536 — nothing already on disk is fetched again.
                Assert.That(cdn.Requests.Select(r => r.Header("Range")), Is.EqualTo(new[] { "bytes=0-39", "bytes=65536-200167" }));

                // An existing file is replaced, and a changed object discards the part and its ETag.
                cdn.Requests.Clear();
                var second = Put(cdn, "a.bin", TestEncryptor.Pattern(200000, 9));
                Assert.ThrowsAsync<InvalidOperationException>(
                    () => AssetFiles.DownloadToFileAsync(client, "a.bin", destination, _ => throw new InvalidOperationException("stop")));
                Assert.That(File.ReadAllText(destination + ".part.etag"), Is.EqualTo("\"etag-2\""));
                AssetFiles.DownloadToFileAsync(client, "a.bin", destination).Wait();
                Assert.That(File.ReadAllBytes(destination), Is.EqualTo(second));
            }
            finally
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, true);
                }
            }
        }

        // ---- lifetime and logging ---------------------------------------------------

        [Test]
        public void ADisposedClientRefusesEveryCall()
        {
            var client = Client(new FakeCdn());
            client.Dispose();
            client.Dispose();

            Assert.Throws<ObjectDisposedException>(() => client.ReadAsync("a.bin"));
            Assert.Throws<ObjectDisposedException>(() => client.DownloadAsync("a.bin", new MemorySink()));
        }

        [Test]
        public void NeverLogsTheKeyTheUrlOrAByteOfPlaintext()
        {
            var log = new CapturingLogWriter();
            var cdn = new FakeCdn();
            var marker = Encoding.UTF8.GetBytes("plaintext-marker plaintext-marker" + new string('p', 70000));
            Put(cdn, "a.bin", marker);
            Put(cdn, "b.bin", marker);
            var client = Client(cdn, logger: Capture(log));

            client.ReadAsync("a.bin").Wait();
            client.ReadRangeAsync("a.bin", 1, 5).Wait();
            client.DownloadAsync("a.bin", new MemorySink()).Wait();

            // The warn and info paths too: a change under a read, and a transport failure.
            cdn.BeforeAnswer = (index, request) =>
            {
                if (request.Header("Range")?.StartsWith("bytes=65536-") == true)
                {
                    Put(cdn, "b.bin", marker);
                    cdn.BeforeAnswer = null;
                }
            };
            client.ReadRangeAsync("b.bin", 70000, 70010).Wait();
            cdn.RefuseRange = true;
            Assert.ThrowsAsync<AssetClientException>(() => client.ReadRangeAsync("a.bin", 0, 5));

            // Positive controls: every kind of line was written at all.
            Assert.That(log.Lines.Count(l => l.Contains("asset request {")), Is.GreaterThanOrEqualTo(3));
            Assert.That(log.Text, Does.Contain("asset changed during a read; starting over"));
            Assert.That(log.Text, Does.Contain("asset request failed"));
            Assert.That(log.Text, Does.Not.Contain("yak1"));
            Assert.That(log.Text, Does.Not.Contain(Key.Text.Substring(5, 20)));
            Assert.That(log.Text, Does.Not.Contain(BitConverter.ToString(Key.Bytes, 0, 8).Replace("-", string.Empty).ToLowerInvariant()));
            Assert.That(log.Text, Does.Not.Contain("cdn.test"));
            Assert.That(log.Text, Does.Not.Contain("plaintext-marker"));
        }

        [Test]
        public void AHugeWindowIsClampedNotOverflowed()
        {
            var cdn = new FakeCdn();
            var plain = Put(cdn, "a.bin", TestEncryptor.Pattern(1000));

            Assert.That(Client(cdn).ReadRangeAsync("a.bin", 0, 1L << 47).Result, Is.EqualTo(plain));
            Assert.That(Client(cdn).ReadRangeAsync("a.bin", 1L << 47, null).Result, Is.Empty);
        }

        [Test]
        public void AClientDisposedDuringAReadNeverResetsTheSink()
        {
            var cdn = new FakeCdn();
            Put(cdn, "a.bin", TestEncryptor.Pattern(200000, 1));
            var client = Client(cdn);
            var sink = new MemorySink();
            sink.Seed(new byte[100000]);
            cdn.BeforeAnswer = (index, _) =>
            {
                // The segments request: the object changes and the client goes away.
                if (index == 1)
                {
                    Put(cdn, "a.bin", TestEncryptor.Pattern(200000, 2));
                    client.Dispose();
                }
            };

            Assert.ThrowsAsync<ObjectDisposedException>(
                () => client.DownloadAsync("a.bin", sink, new AssetDownloadOptions { Resume = new AssetResume(100000, "\"etag-1\"") }));
            Assert.That(sink.Events, Does.Not.Contain("reset"));
        }

        [Test]
        public void ALogPathIsCappedAndStripped()
        {
            var log = new CapturingLogWriter();
            var cdn = new FakeCdn();
            var path = "a‮b/" + new string('x', 100);

            Assert.ThrowsAsync<AssetClientException>(() => Client(cdn, logger: Capture(log)).ReadAsync(path));

            Assert.That(log.Text, Does.Contain("a?b/"));
            Assert.That(log.Text, Does.Not.Contain(new string('x', 70)));
        }

        // ---- timeouts over a transport that really yields ---------------------------

        private sealed class StallingTransport : IAssetTransport
        {
            internal bool StallHeaders { get; set; }

            public async Task<IAssetResponse> SendAsync(AssetHttpRequest request, CancellationToken cancellationToken)
            {
                if (StallHeaders)
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }

                return new Stalled();
            }

            private sealed class Stalled : IAssetResponse
            {
                public int Status => 200;

                public string? GetHeader(string name) => null;

                public async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                    return 0;
                }

                public void Dispose()
                {
                }
            }
        }

        [Test]
        public async Task AQuietBodyEndsAsNetworkAfterTheIdleTimeout()
        {
            var client = AssetBundleClient.Create(new AssetBundleClientOptions
            {
                BaseUrl = Base,
                Transport = new StallingTransport(),
                BodyIdleTimeout = TimeSpan.FromMilliseconds(50),
            });

            var error = await Fails.WithAssetException(() => client.ReadAsync("a.bin"));

            Assert.That(error.Code, Is.EqualTo(AssetErrorCodes.Network));
            Assert.That(error.Detail, Is.EqualTo("the body stalled"));
        }

        [Test]
        public async Task ATimedOutRangedRequestInCorsSafeModeIsNotMistakenForARefusedPreflight()
        {
            // The HEAD answers; the ranged GET never does. A refused preflight would fall
            // back to the whole file — a timeout must not.
            var transport = new HeadThenStall();
            var client = AssetBundleClient.Create(new AssetBundleClientOptions
            {
                BaseUrl = Base,
                Key = Key.Text,
                Transport = transport,
                CorsSafe = true,
                ResponseTimeout = TimeSpan.FromMilliseconds(50),
            });

            var error = await Fails.WithAssetException(() => client.ReadRangeAsync("a.bin", 0, 10));

            Assert.That(error.Code, Is.EqualTo(AssetErrorCodes.Network));
            Assert.That(error.Detail, Is.EqualTo("no response in time"));
            Assert.That(transport.Methods, Is.EqualTo(new[] { "HEAD", "GET" }));
        }

        private sealed class HeadThenStall : IAssetTransport
        {
            internal System.Collections.Generic.List<string> Methods { get; } = new System.Collections.Generic.List<string>();

            public async Task<IAssetResponse> SendAsync(AssetHttpRequest request, CancellationToken cancellationToken)
            {
                Methods.Add(request.Method);
                if (request.Method == "HEAD")
                {
                    return new Head();
                }

                // Ignores its token, as a stream that only checks on entry would.
                await Task.Delay(Timeout.Infinite);
                return new Head();
            }

            private sealed class Head : IAssetResponse
            {
                public int Status => 200;

                public string? GetHeader(string name) => name == "Content-Length" ? "1000" : name == "ETag" ? "\"e\"" : null;

                public Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => Task.FromResult(0);

                public void Dispose()
                {
                }
            }
        }

        [Test]
        public async Task NoHeadersWithinTheResponseTimeoutIsNetwork()
        {
            var client = AssetBundleClient.Create(new AssetBundleClientOptions
            {
                BaseUrl = Base,
                Transport = new StallingTransport { StallHeaders = true },
                ResponseTimeout = TimeSpan.FromMilliseconds(50),
            });

            var error = await Fails.WithAssetException(() => client.ReadAsync("a.bin"));

            Assert.That(error.Code, Is.EqualTo(AssetErrorCodes.Network));
        }

        // ---- the review round's gaps ----------------------------------------------------

        /// <summary>A body whose reads ignore their token, as a stream that checks it only on entry does.</summary>
        private sealed class DeafTransport : IAssetTransport
        {
            internal bool StallHeaders { get; set; }

            internal bool TimeoutOnRange { get; set; }

            internal int Disposed;

            internal System.Collections.Generic.List<string> Methods { get; } = new System.Collections.Generic.List<string>();

            public async Task<IAssetResponse> SendAsync(AssetHttpRequest request, CancellationToken cancellationToken)
            {
                Methods.Add(request.Method);
                if (request.Method == "HEAD")
                {
                    return new Deaf(this, stallBody: false);
                }

                if (TimeoutOnRange)
                {
                    throw new TimeoutException("transport timer");
                }

                if (StallHeaders)
                {
                    await Task.Delay(300);
                }

                return new Deaf(this, stallBody: !StallHeaders);
            }

            private sealed class Deaf : IAssetResponse
            {
                private readonly DeafTransport _owner;
                private readonly bool _stallBody;

                internal Deaf(DeafTransport owner, bool stallBody)
                {
                    _owner = owner;
                    _stallBody = stallBody;
                }

                public int Status => 200;

                public string? GetHeader(string name) => name == "Content-Length" ? "1000" : name == "ETag" ? "\"e\"" : null;

                public async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
                {
                    if (_stallBody)
                    {
                        await Task.Delay(Timeout.Infinite);
                    }

                    return 0;
                }

                public void Dispose() => Interlocked.Increment(ref _owner.Disposed);
            }
        }

        [Test]
        public async Task TheIdleBoundHoldsEvenWhenTheStreamIgnoresItsToken()
        {
            var client = AssetBundleClient.Create(new AssetBundleClientOptions
            {
                BaseUrl = Base,
                Transport = new DeafTransport(),
                BodyIdleTimeout = TimeSpan.FromMilliseconds(50),
            });

            var error = await Fails.WithAssetException(() => client.ReadAsync("a.bin"));

            Assert.That(error.Detail, Is.EqualTo("the body stalled"));
        }

        [Test]
        public async Task ATransportsOwnTimeoutIsNotARefusedPreflight()
        {
            var transport = new DeafTransport { TimeoutOnRange = true };
            var client = AssetBundleClient.Create(new AssetBundleClientOptions { BaseUrl = Base, Key = Key.Text, Transport = transport, CorsSafe = true });

            var error = await Fails.WithAssetException(() => client.ReadRangeAsync("a.bin", 0, 10));

            Assert.That(error.Detail, Is.EqualTo("no response in time"));
            Assert.That(transport.Methods, Is.EqualTo(new[] { "HEAD", "GET" }));
        }

        [Test]
        public async Task AResponseArrivingAfterTheTimeoutIsReleased()
        {
            var transport = new DeafTransport { StallHeaders = true };
            var client = AssetBundleClient.Create(new AssetBundleClientOptions { BaseUrl = Base, Transport = transport, ResponseTimeout = TimeSpan.FromMilliseconds(50) });

            await Fails.WithAssetException(() => client.ReadAsync("a.bin"));
            await Task.Delay(600);

            Assert.That(transport.Disposed, Is.EqualTo(1));
        }

        [Test]
        public void APlainRangeAnsweredAfterDisposeIsRefused()
        {
            var cdn = new FakeCdn();
            cdn.Put(Url("a.bin"), TestEncryptor.Pattern(100));
            var client = Client(cdn, encrypted: false);
            cdn.BeforeAnswer = (_, __) => client.Dispose();

            Assert.ThrowsAsync<ObjectDisposedException>(() => client.ReadRangeAsync("a.bin", 200, null));
            Assert.That(cdn.OpenBodies, Is.EqualTo(0));
        }

        [Test]
        public void APlainDownloadAnsweredWholeReportsTheNewObjectsEtagNotTheResumes()
        {
            var cdn = new FakeCdn { NoEtag = true };
            var plain = TestEncryptor.Pattern(500);
            cdn.Put(Url("a.bin"), plain);
            var sink = new MemorySink();
            sink.Seed(new byte[100]);

            var result = Client(cdn, encrypted: false).DownloadAsync("a.bin", sink, new AssetDownloadOptions { Resume = new AssetResume(100, "\"stale\"") }).Result;

            Assert.That(sink.Bytes, Is.EqualTo(plain));
            Assert.That(result.ETag, Is.Null);
        }

        [Test]
        public void APartLongerThanTheFileIsDiscardedAndTheDownloadRedone()
        {
            var folder = Path.Combine(Path.GetTempPath(), "yyt-asset-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(folder);
                var destination = Path.Combine(folder, "a.bin");
                var cdn = new FakeCdn();
                var plain = Put(cdn, "a.bin", TestEncryptor.Pattern(1000));
                File.WriteAllBytes(destination + ".part", new byte[5000]);
                File.WriteAllText(destination + ".part.etag", "\"etag-1\"");

                AssetFiles.DownloadToFileAsync(Client(cdn), "a.bin", destination).Wait();

                Assert.That(File.ReadAllBytes(destination), Is.EqualTo(plain));
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        [Test]
        public void ASideFileThatIsALinkIsNeverWrittenThrough()
        {
#if NET6_0_OR_GREATER
            var folder = Path.Combine(Path.GetTempPath(), "yyt-asset-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(folder);
                var destination = Path.Combine(folder, "a.bin");
                var victim = Path.Combine(folder, "victim.txt");
                File.WriteAllText(victim, "untouched");
                try
                {
                    File.CreateSymbolicLink(destination + ".part", victim);
                }
                catch (Exception)
                {
                    Assert.Ignore("symbolic links are not available here");
                }

                var cdn = new FakeCdn();
                var plain = Put(cdn, "a.bin", TestEncryptor.Pattern(1000));
                AssetFiles.DownloadToFileAsync(Client(cdn), "a.bin", destination).Wait();

                Assert.That(File.ReadAllText(victim), Is.EqualTo("untouched"));
                Assert.That(File.ReadAllBytes(destination), Is.EqualTo(plain));
            }
            finally
            {
                Directory.Delete(folder, true);
            }
#else
            Assert.Ignore("File.CreateSymbolicLink needs .NET 6; the editor's profile has none");
#endif
        }
    }
}
