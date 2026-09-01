using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;

namespace Yingyeothon.PublicApi.Tests
{
    /// <summary>
    /// Snapshots each runtime assembly's public surface and fails on an unreviewed
    /// change, and checks that every public type is named in its package README.
    /// </summary>
    /// <remarks>
    /// Reflection is fine here and nowhere else: this is a test assembly, so IL2CPP's
    /// managed stripper never sees it and <c>scripts/validate-packages.sh</c>'s grep
    /// only walks <c>packages/*/Runtime</c>. A drifted <c>## Public API</c> listing was
    /// a real defect class in tslib, which is why the README is a gate and not a
    /// courtesy.
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
            var actual = Describe(Load(assembly));
            var approvedPath = Path.Combine(AppContext.BaseDirectory, "Approved", assembly + ".approved.txt");

            if (!File.Exists(approvedPath))
            {
                Assert.Fail(WriteReceived(assembly, actual, "there is no approved snapshot yet"));
            }

            var approved = Normalize(File.ReadAllText(approvedPath));
            if (approved != Normalize(actual))
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
            var readmePath = Path.Combine(RepositoryRoot(), "packages", package, "README.md");
            var readme = File.ReadAllText(readmePath);

            var missing = Load(assembly)
                .GetExportedTypes()
                .Select(NameOf)
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

        private static Assembly Load(string name) => Assembly.Load(new AssemblyName(name));

        /// <summary>
        /// One line per public member, sorted, so a diff points at the symbol that
        /// changed rather than at the whole file.
        /// </summary>
        private static string Describe(Assembly assembly)
        {
            var lines = new List<string>();
            foreach (var type in assembly.GetExportedTypes())
            {
                var typeName = NameOf(type);
                lines.Add(Kind(type) + " " + typeName);

                const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static
                    | BindingFlags.DeclaredOnly;
                foreach (var member in type.GetMembers(flags))
                {
                    if (member is MethodInfo method && method.IsSpecialName)
                    {
                        // Property and event accessors are covered by their own entry.
                        continue;
                    }

                    lines.Add("  " + typeName + "." + Signature(member));
                }
            }

            lines.Sort(StringComparer.Ordinal);
            return string.Join("\n", lines) + "\n";
        }

        private static string Kind(Type type)
        {
            if (type.IsEnum)
            {
                return "enum";
            }

            if (type.IsInterface)
            {
                return "interface";
            }

            if (type.IsValueType)
            {
                return "struct";
            }

            return type.IsAbstract && type.IsSealed ? "static class" : "class";
        }

        private static string Signature(MemberInfo member)
        {
            switch (member)
            {
                case MethodInfo method:
                    return method.Name + "(" + Parameters(method.GetParameters()) + ") : " + NameOf(method.ReturnType);
                case ConstructorInfo constructor:
                    return "ctor(" + Parameters(constructor.GetParameters()) + ")";
                case PropertyInfo property:
                    return property.Name + " : " + NameOf(property.PropertyType)
                        + (property.CanRead ? " get" : string.Empty)
                        + (property.CanWrite ? " set" : string.Empty);
                case FieldInfo field:
                    return field.Name + " : " + NameOf(field.FieldType);
                case EventInfo declared:
                    return "event " + declared.Name + " : " + NameOf(declared.EventHandlerType!);
                default:
                    return member.Name;
            }
        }

        private static string Parameters(ParameterInfo[] parameters)
            => string.Join(", ", parameters.Select(p => NameOf(p.ParameterType) + (p.IsOptional ? "?" : string.Empty)));

        private static string NameOf(Type type)
        {
            if (type.IsGenericType)
            {
                var name = type.Name.Substring(0, type.Name.IndexOf('`'));
                return name + "<" + string.Join(", ", type.GetGenericArguments().Select(NameOf)) + ">";
            }

            return type.Name;
        }

        private static string Normalize(string text) => text.Replace("\r\n", "\n").TrimEnd() + "\n";

        // ---- reporting ------------------------------------------------------

        private static string WriteReceived(string assembly, string actual, string why)
        {
            var received = Path.Combine(
                RepositoryRoot(), "tests", "Yingyeothon.PublicApi.Tests", "Approved", assembly + ".received.txt");
            File.WriteAllText(received, actual, new UTF8Encoding(false));
            return why + " for " + assembly + ". Review the change, update the package README's"
                + " `## Public API` section, then approve it with:\n"
                + "  mv " + received + " " + received.Replace(".received.txt", ".approved.txt");
        }

        /// <summary>
        /// Walks up from the test binary to the repository root. The build output
        /// lives under <c>artifacts/</c>, so the marker is the solution file.
        /// </summary>
        private static string RepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Yingyeothon.sln")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName ?? throw new InvalidOperationException("Yingyeothon.sln not found above the test binary");
        }
    }
}
