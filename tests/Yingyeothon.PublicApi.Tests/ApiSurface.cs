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
    /// Renders an assembly's public surface. Shared by the approved-snapshot gate and
    /// the generated API reference so the two can never describe a member differently.
    /// </summary>
    /// <remarks>
    /// Reflection is fine here and nowhere else: this is a test assembly, so IL2CPP's
    /// managed stripper never sees it and <c>scripts/validate-packages.sh</c>'s grep
    /// only walks <c>packages/*/Runtime</c>.
    /// </remarks>
    internal static class ApiSurface
    {
        /// <summary>Members declared directly on a type, public and not inherited.</summary>
        internal const BindingFlags DeclaredPublic =
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        /// <summary>Every shipped assembly with its UPM package: the axis each gate here runs along.</summary>
        internal static IEnumerable<TestCaseData> Packages
        {
            get
            {
                yield return new TestCaseData("Yingyeothon.Codec", "com.yingyeothon.codec");
                yield return new TestCaseData("Yingyeothon.Logger", "com.yingyeothon.logger");
                yield return new TestCaseData("Yingyeothon.EventBroker", "com.yingyeothon.event-broker");
                yield return new TestCaseData("Yingyeothon.Gamebase.Client", "com.yingyeothon.gamebase-client");
            }
        }

        internal static Assembly Load(string name) => Assembly.Load(new AssemblyName(name));

        /// <summary>Writes what a gate actually saw, UTF-8 without a BOM so approving it is a plain rename.</summary>
        internal static void WriteReceived(string path, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content, new UTF8Encoding(false));
        }

        /// <summary>The exported types, ordered so a diff points at one symbol.</summary>
        internal static IEnumerable<Type> Types(Assembly assembly)
            => assembly.GetExportedTypes().OrderBy(NameOf, StringComparer.Ordinal);

        /// <summary>
        /// The members worth listing: property and event accessors are covered by their
        /// own entry, so the compiler-generated methods behind them are skipped.
        /// </summary>
        internal static IEnumerable<MemberInfo> Members(Type type)
            => type.GetMembers(DeclaredPublic)
                .Where(member => !(member is MethodInfo method && method.IsSpecialName));

        internal static string Kind(Type type)
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

        internal static string Signature(MemberInfo member)
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

        internal static string Parameters(ParameterInfo[] parameters)
            => string.Join(", ", parameters.Select(p => NameOf(p.ParameterType) + (p.IsOptional ? "?" : string.Empty)));

        internal static string NameOf(Type type)
        {
            if (type.IsGenericType)
            {
                var name = type.Name.Substring(0, type.Name.IndexOf('`'));
                return name + "<" + string.Join(", ", type.GetGenericArguments().Select(NameOf)) + ">";
            }

            return type.Name;
        }

        internal static string Normalize(string text) => text.Replace("\r\n", "\n").TrimEnd() + "\n";

        /// <summary>
        /// Walks up from the test binary to the repository root. The build output lives
        /// under <c>artifacts/</c>, so the marker is the solution file.
        /// </summary>
        internal static string RepositoryRoot()
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
