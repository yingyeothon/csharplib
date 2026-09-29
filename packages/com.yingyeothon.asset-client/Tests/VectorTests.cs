using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Yingyeothon.Codec;

namespace Yingyeothon.Assets.Tests
{
    /// <summary>
    /// The <c>yyt-enc v1</c> conformance vectors, copied verbatim from the service
    /// repository's <c>docs/asset-encryption-vectors.json</c> at commit <c>f6c418a</c>
    /// (2026-09-28). Every decryptor — the Go CLI, the console's node:crypto check, tink-go,
    /// and the three client libraries — must accept every positive case and refuse every
    /// negative one with <c>asset_corrupt</c>. Refresh the copy when that file changes;
    /// never edit it here.
    /// </summary>
    [TestFixture]
    public class VectorTests
    {
        // A live bundle: the associated data is the path itself, `v3/…` included.
        internal const string Base = "https://dev-d.yyt.life/assets/bnd_vectors/";

        private static readonly Lazy<JsonValue> Vectors = new Lazy<JsonValue>(() =>
            Json.ParseBig(File.ReadAllText(Fixture("asset-encryption-vectors.json")), Json.MaxBigLength));

        /// <summary>
        /// The fixture beside the tests: copied to the output directory under dotnet, read in
        /// place from the package inside the Unity editor.
        /// </summary>
        internal static string Fixture(string name)
        {
            var candidates = new[]
            {
                Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", name),
                Path.Combine("Packages", "com.yingyeothon.asset-client", "Tests", "Fixtures", name),
            };
            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            Assert.Fail("fixture not found: " + name);
            return null!;
        }

        public static IEnumerable<TestCaseData> Positive()
            => Vectors.Value.GetMemberOrNull("cases")!.AsArray().Select(c => new TestCaseData(c.GetString("name")!).SetName("Accepts " + c.GetString("name")));

        public static IEnumerable<TestCaseData> Negative()
            => Vectors.Value.GetMemberOrNull("negative")!.AsArray().Select(c => new TestCaseData(c.GetString("name")!).SetName("Refuses " + c.GetString("name")));

        private static JsonValue Case(string list, string name)
            => Vectors.Value.GetMemberOrNull(list)!.AsArray().Single(c => c.GetString("name") == name);

        internal static byte[] Hex(string text)
        {
            var output = new byte[text.Length / 2];
            for (var i = 0; i < output.Length; i++)
            {
                output[i] = Convert.ToByte(text.Substring(i * 2, 2), 16);
            }

            return output;
        }

        /// <summary><c>plaintextUnitHex</c> repeated and cut; the empty case has an empty unit.</summary>
        private static byte[] PlaintextOf(string unitHex, int length)
        {
            var output = new byte[length];
            if (length == 0)
            {
                return output;
            }

            var unit = Hex(unitHex);
            for (var at = 0; at < length; at += unit.Length)
            {
                Buffer.BlockCopy(unit, 0, output, at, Math.Min(unit.Length, length - at));
            }

            return output;
        }

        private static IAssetBundleClient Served(string? key, byte[]? keyBytes, string path, byte[] ciphertext, bool corsSafe, out FakeCdn cdn)
        {
            cdn = new FakeCdn { CrossOrigin = corsSafe };
            cdn.Put(Base + string.Join("/", path.Split('/').Select(Uri.EscapeDataString)), ciphertext);
            return AssetBundleClient.Create(new AssetBundleClientOptions
            {
                BaseUrl = Base,
                Key = key,
                KeyBytes = keyBytes,
                Transport = cdn,
                CorsSafe = corsSafe,
            });
        }

        [Test]
        public void CoversTheCasesTheFormatDocumentLists()
        {
            Assert.That(Vectors.Value.GetString("format"), Is.EqualTo("yyt-enc-v1"));
            Assert.That(Vectors.Value.GetMemberOrNull("cases")!.AsArray().Count, Is.EqualTo(7));
            Assert.That(Vectors.Value.GetMemberOrNull("negative")!.AsArray().Count, Is.EqualTo(9));
        }

        [TestCaseSource(nameof(Positive))]
        public void ReadsItWholeWithTheKeyAsTextAndAsBytes(string name)
        {
            var vector = Case("cases", name);
            var path = vector.GetString("path")!;
            var plaintext = PlaintextOf(vector.GetString("plaintextUnitHex")!, (int)vector.GetNumber("plaintextLength")!.Value);
            var ciphertext = Hex(vector.GetString("ciphertextHex")!);

            var asText = Served(vector.GetString("key"), null, path, ciphertext, false, out var cdn1);
            var asBytes = Served(null, Hex(vector.GetString("keyHex")!), path, ciphertext, false, out var cdn2);

            Assert.That(asText.ReadAsync(path).Result, Is.EqualTo(plaintext));
            Assert.That(asBytes.ReadAsync(path).Result, Is.EqualTo(plaintext));
            Assert.That(cdn1.OpenBodies + cdn2.OpenBodies, Is.EqualTo(0));
        }

        [TestCaseSource(nameof(Positive))]
        public void IsReproducedByteForByteByTheTestsOwnEncryptor(string name)
        {
            var vector = Case("cases", name);
            var plaintext = PlaintextOf(vector.GetString("plaintextUnitHex")!, (int)vector.GetNumber("plaintextLength")!.Value);

            var ciphertext = TestEncryptor.Encrypt(Hex(vector.GetString("keyHex")!), vector.GetString("path")!, plaintext);

            Assert.That(ciphertext, Is.EqualTo(Hex(vector.GetString("ciphertextHex")!)));
        }

        [TestCaseSource(nameof(Positive))]
        public void ReadsEveryRangeAcrossItsSegmentBoundaries(string name)
        {
            var vector = Case("cases", name);
            var path = vector.GetString("path")!;
            var n = (int)vector.GetNumber("plaintextLength")!.Value;
            var plaintext = PlaintextOf(vector.GetString("plaintextUnitHex")!, n);
            var ciphertext = Hex(vector.GetString("ciphertextHex")!);
            var cuts = new[] { 0, 1, 65463, 65464, 65465, 130967, 130968, 130969, n - 1, n }.Where(c => c >= 0 && c <= n).Distinct().ToArray();

            foreach (var corsSafe in new[] { false, true })
            {
                var client = Served(vector.GetString("key"), null, path, ciphertext, corsSafe, out var cdn);
                foreach (var start in cuts)
                {
                    foreach (var end in cuts)
                    {
                        if (end <= start)
                        {
                            continue;
                        }

                        var bytes = client.ReadRangeAsync(path, start, end).Result;
                        Assert.That(bytes, Is.EqualTo(plaintext.AsSpan(start, end - start).ToArray()), start + "-" + end + " corsSafe " + corsSafe);
                    }
                }

                Assert.That(client.ReadRangeAsync(path, 0, null).Result, Is.EqualTo(plaintext));
                Assert.That(cdn.OpenBodies, Is.EqualTo(0));
            }
        }

        [TestCaseSource(nameof(Positive))]
        public void DownloadsIt(string name)
        {
            var vector = Case("cases", name);
            var path = vector.GetString("path")!;
            var plaintext = PlaintextOf(vector.GetString("plaintextUnitHex")!, (int)vector.GetNumber("plaintextLength")!.Value);
            var ciphertext = Hex(vector.GetString("ciphertextHex")!);

            foreach (var corsSafe in new[] { false, true })
            {
                var sink = new MemorySink();
                var result = Served(vector.GetString("key"), null, path, ciphertext, corsSafe, out _).DownloadAsync(path, sink).Result;

                Assert.That(result.Bytes, Is.EqualTo(plaintext.LongLength));
                Assert.That(sink.Bytes, Is.EqualTo(plaintext));
            }
        }

        [TestCaseSource(nameof(Negative))]
        public void AsAssetCorruptOnEveryRead(string name)
        {
            var vector = Case("negative", name);
            Assert.That(vector.GetString("error"), Is.EqualTo("asset_corrupt"));
            var path = vector.GetString("path")!;
            var ciphertext = Hex(vector.GetString("ciphertextHex")!);

            foreach (var corsSafe in new[] { false, true })
            {
                var client = Served(vector.GetString("key"), null, path, ciphertext, corsSafe, out var cdn);
                var attempts = new Func<System.Threading.Tasks.Task>[]
                {
                    () => client.ReadAsync(path),
                    () => client.ReadRangeAsync(path, 0, null),
                    () => client.DownloadAsync(path, new MemorySink()),
                };
                foreach (var attempt in attempts)
                {
                    var error = Assert.ThrowsAsync<AssetClientException>(() => attempt());
                    Assert.That(error!.Code, Is.EqualTo(AssetErrorCodes.AssetCorrupt), name + " corsSafe " + corsSafe);
                }

                Assert.That(cdn.OpenBodies, Is.EqualTo(0));
            }
        }
    }
}
