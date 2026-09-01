using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;

namespace Yingyeothon.PublicApi.Tests
{
    /// <summary>
    /// Renders <c>docs/api/&lt;assembly&gt;.md</c> from the assembly itself and fails
    /// when the committed file differs.
    /// </summary>
    /// <remarks>
    /// A hand-maintained reference drifts, and a reference that drifts is worse than
    /// none: a reader trusts it. Generating it from the same reflection walk the
    /// approved snapshot uses, plus the compiler's own XML documentation, means the
    /// reference cannot disagree with the code, and approving a change is the same
    /// rename the snapshot gate already asks for.
    /// </remarks>
    [TestFixture]
    public class ApiReferenceTests
    {
        [TestCase("Yingyeothon.Codec", "com.yingyeothon.codec")]
        [TestCase("Yingyeothon.Logger", "com.yingyeothon.logger")]
        [TestCase("Yingyeothon.EventBroker", "com.yingyeothon.event-broker")]
        [TestCase("Yingyeothon.Gamebase.Client", "com.yingyeothon.gamebase-client")]
        public void TheGeneratedReferenceMatchesTheAssembly(string assembly, string package)
        {
            var actual = Render(assembly, package);
            var path = Path.Combine(ApiSurface.RepositoryRoot(), "docs", "api", assembly + ".md");

            if (!File.Exists(path))
            {
                Assert.Fail(WriteReceived(path, actual, "there is no generated reference yet"));
            }

            if (ApiSurface.Normalize(File.ReadAllText(path)) != ApiSurface.Normalize(actual))
            {
                Assert.Fail(WriteReceived(path, actual, "the public surface or its documentation changed"));
            }
        }

        // ---- rendering ------------------------------------------------------

        private static string Render(string assemblyName, string package)
        {
            var assembly = ApiSurface.Load(assemblyName);
            var docs = XmlDocs.Load(assemblyName);
            var text = new StringBuilder();

            text.Append("# ").Append(assemblyName).Append("\n\n");
            text.Append("<!-- Generated from the assembly by tests/Yingyeothon.PublicApi.Tests.\n");
            text.Append("     Do not edit by hand: the test rewrites it and CI compares it. -->\n\n");
            text.Append("Every public type and member, with its documentation comment — the same text\n");
            text.Append("your IDE shows. For what the package is *for*, read\n");
            text.Append("[the guide](../README.md) and\n");
            text.Append("[`packages/").Append(package).Append("/README.md`](../../packages/").Append(package).Append("/README.md).\n\n");

            var types = new List<Type>(ApiSurface.Types(assembly));

            text.Append("## Contents\n\n");
            foreach (var type in types)
            {
                text.Append("- [`").Append(ApiSurface.NameOf(type)).Append("`](#")
                    .Append(Anchor(ApiSurface.Kind(type) + " " + ApiSurface.NameOf(type))).Append(")\n");
            }

            foreach (var type in types)
            {
                var name = ApiSurface.NameOf(type);
                text.Append("\n## ").Append(ApiSurface.Kind(type)).Append(' ').Append(name).Append("\n\n");

                var summary = docs.For(type);
                if (summary.Length > 0)
                {
                    text.Append(summary).Append("\n\n");
                }

                var members = new List<string>();
                foreach (var member in ApiSurface.Members(type))
                {
                    // `value__` is an enum's backing field, an artifact of reflecting
                    // over one rather than a member anybody can name.
                    if (type.IsEnum && member.Name == "value__")
                    {
                        continue;
                    }

                    var memberSummary = docs.For(type, member);
                    members.Add(type.IsEnum
                        ? "- `" + member.Name + "`" + (memberSummary.Length > 0 ? " — " + memberSummary : string.Empty)
                        : "| `" + ApiSurface.Signature(member) + "` | " + Cell(memberSummary) + " |");
                }

                if (members.Count == 0)
                {
                    text.Append("No public members.\n");
                    continue;
                }

                members.Sort(StringComparer.Ordinal);
                if (!type.IsEnum)
                {
                    text.Append("| Member | Summary |\n| --- | --- |\n");
                }

                foreach (var row in members)
                {
                    text.Append(row).Append('\n');
                }
            }

            return text.ToString();
        }

        /// <summary>A table cell cannot carry a pipe or a line break.</summary>
        private static string Cell(string summary)
            => summary.Replace("|", "\\|").Replace("\n", " ");

        /// <summary>GitHub's heading anchor: lowercased, punctuation dropped, spaces to dashes.</summary>
        private static string Anchor(string heading)
        {
            var anchor = new StringBuilder();
            foreach (var c in heading)
            {
                if (char.IsLetterOrDigit(c))
                {
                    anchor.Append(char.ToLowerInvariant(c));
                }
                else if (c == ' ' || c == '-')
                {
                    anchor.Append('-');
                }
            }

            return anchor.ToString();
        }

        private static string WriteReceived(string path, string actual, string why)
        {
            var received = path.Replace(".md", ".received.md");
            Directory.CreateDirectory(Path.GetDirectoryName(received)!);
            File.WriteAllText(received, actual, new UTF8Encoding(false));
            return why + ". Review it, then approve with:\n  mv " + received + " " + path;
        }
    }
}
