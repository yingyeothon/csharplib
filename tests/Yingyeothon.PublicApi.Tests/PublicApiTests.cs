using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace Yingyeothon.PublicApi.Tests
{
    /// <summary>
    /// Snapshots each runtime assembly's public surface and fails on an unreviewed
    /// change, and checks that every public type is named in its package README.
    /// </summary>
    /// <remarks>
    /// A drifted <c>## Public API</c> listing was a real defect class in tslib, which
    /// is why the README is a gate and not a courtesy. The rendering itself lives in
    /// <see cref="ApiSurface"/>, shared with the generated reference in
    /// <see cref="ApiReferenceTests"/>.
    /// </remarks>
    [TestFixture]
    public class PublicApiTests
    {
        [TestCase("Yingyeothon.Codec")]
        [TestCase("Yingyeothon.Logger")]
        [TestCase("Yingyeothon.EventBroker")]
        [TestCase("Yingyeothon.Gamebase.Client")]
        public void ThePublicSurfaceMatchesItsApprovedSnapshot(string assembly)
        {
            var actual = Describe(assembly);
            var approvedPath = Path.Combine(AppContext.BaseDirectory, "Approved", assembly + ".approved.txt");

            if (!File.Exists(approvedPath))
            {
                Assert.Fail(WriteReceived(assembly, actual, "there is no approved snapshot yet"));
            }

            var approved = ApiSurface.Normalize(File.ReadAllText(approvedPath));
            if (approved != ApiSurface.Normalize(actual))
            {
                Assert.Fail(WriteReceived(assembly, actual, "the public surface changed"));
            }
        }

        [TestCase("Yingyeothon.Codec", "com.yingyeothon.codec")]
        [TestCase("Yingyeothon.Logger", "com.yingyeothon.logger")]
        [TestCase("Yingyeothon.EventBroker", "com.yingyeothon.event-broker")]
        [TestCase("Yingyeothon.Gamebase.Client", "com.yingyeothon.gamebase-client")]
        public void EveryPublicTypeIsNamedInThePackageReadme(string assembly, string package)
        {
            var readmePath = Path.Combine(ApiSurface.RepositoryRoot(), "packages", package, "README.md");
            var readme = File.ReadAllText(readmePath);

            var missing = ApiSurface.Load(assembly)
                .GetExportedTypes()
                .Select(ApiSurface.NameOf)
                .Distinct()
                .Where(name => !readme.Contains(name))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

            Assert.That(
                missing,
                Is.Empty,
                "packages/" + package + "/README.md does not mention: " + string.Join(", ", missing));
        }

        // ---- rendering ------------------------------------------------------

        /// <summary>
        /// One line per public member, sorted, so a diff points at the symbol that
        /// changed rather than at the whole file.
        /// </summary>
        private static string Describe(string assembly)
        {
            var lines = new System.Collections.Generic.List<string>();
            foreach (var type in ApiSurface.Types(ApiSurface.Load(assembly)))
            {
                var typeName = ApiSurface.NameOf(type);
                lines.Add(ApiSurface.Kind(type) + " " + typeName);
                foreach (var member in ApiSurface.Members(type))
                {
                    lines.Add("  " + typeName + "." + ApiSurface.Signature(member));
                }
            }

            lines.Sort(StringComparer.Ordinal);
            return string.Join("\n", lines) + "\n";
        }

        // ---- reporting ------------------------------------------------------

        private static string WriteReceived(string assembly, string actual, string why)
        {
            var received = Path.Combine(
                ApiSurface.RepositoryRoot(), "tests", "Yingyeothon.PublicApi.Tests", "Approved", assembly + ".received.txt");
            File.WriteAllText(received, actual, new UTF8Encoding(false));
            return why + " for " + assembly + ". Review the change, update the package README's"
                + " `## Public API` section, then approve it with:\n"
                + "  mv " + received + " " + received.Replace(".received.txt", ".approved.txt");
        }
    }
}
