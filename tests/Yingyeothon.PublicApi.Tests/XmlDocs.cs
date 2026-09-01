using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml.Linq;

namespace Yingyeothon.PublicApi.Tests
{
    /// <summary>
    /// Reads the compiler's XML documentation file so the generated reference carries
    /// the same summaries a consumer's IDE shows. One source, two audiences.
    /// </summary>
    /// <remarks>
    /// Doc-comment ids name types by their full CLR name and generic parameters
    /// positionally (<c>`0</c>), which reflection spells differently, so members are
    /// indexed twice: by simple name plus normalised parameter list, and by simple name
    /// plus arity. The precise key wins; the arity key covers the generic methods, and
    /// is used only when it is unambiguous.
    /// </remarks>
    internal sealed class XmlDocs
    {
        private readonly Dictionary<string, string> _bySignature = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _byArity = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly HashSet<string> _ambiguousArity = new HashSet<string>(StringComparer.Ordinal);

        private XmlDocs()
        {
        }

        /// <summary>An empty set, for an assembly built without a documentation file.</summary>
        internal static XmlDocs Empty { get; } = new XmlDocs();

        internal static XmlDocs Load(string assemblyName)
        {
            var path = Path.Combine(AppContext.BaseDirectory, assemblyName + ".xml");
            if (!File.Exists(path))
            {
                return Empty;
            }

            var docs = new XmlDocs();
            foreach (var member in XDocument.Load(path).Descendants("member"))
            {
                var id = (string?)member.Attribute("name");
                var summary = member.Element("summary");
                if (id == null || summary == null)
                {
                    continue;
                }

                docs.Add(id, Flatten(summary));
            }

            return docs;
        }

        /// <summary>The summary for a type, or an empty string.</summary>
        internal string For(Type type) => Lookup(ApiSurface.NameOf(type), null);

        /// <summary>The summary for a member of <paramref name="type"/>, or an empty string.</summary>
        internal string For(Type type, MemberInfo member)
        {
            var parameters = member switch
            {
                MethodBase method => method.GetParameters(),
                _ => null,
            };

            var name = member is ConstructorInfo ? "#ctor" : member.Name;
            var key = ApiSurface.NameOf(type) + "." + name;

            if (parameters == null)
            {
                return Lookup(key, null);
            }

            // A doc id for a parameterless method carries no argument list at all, so
            // it is indexed under the bare name rather than under "Name()".
            if (parameters.Length == 0)
            {
                return Lookup(key, null);
            }

            var signature = key + "(" + string.Join(",", parameters.Select(p => Reflected(p.ParameterType))) + ")";
            if (_bySignature.TryGetValue(signature, out var precise))
            {
                return precise;
            }

            return Lookup(key + "#" + parameters.Length, null);
        }

        private string Lookup(string key, string? _)
        {
            if (_bySignature.TryGetValue(key, out var exact))
            {
                return exact;
            }

            return !_ambiguousArity.Contains(key) && _byArity.TryGetValue(key, out var byArity) ? byArity : string.Empty;
        }

        private void Add(string id, string summary)
        {
            // "T:Yingyeothon.Codec.JsonValue", or
            // "M:Yingyeothon.Codec.Json.TryParse(System.String,Yingyeothon.Codec.JsonValue@)".
            // The prefix is what tells a type id from a member id; without it a nested
            // name and a member name are indistinguishable.
            if (id.Length < 2 || id[1] != ':')
            {
                return;
            }

            var kind = id[0];
            var body = id.Substring(2);
            var open = body.IndexOf('(');
            var name = open < 0 ? body : body.Substring(0, open);
            var arguments = open < 0 ? null : body.Substring(open + 1).TrimEnd(')');

            var simple = SimplifyMemberName(name, kind == 'T');
            if (simple == null)
            {
                return;
            }

            if (arguments == null)
            {
                _bySignature[simple] = summary;
                return;
            }

            var parts = SplitArguments(arguments).Select(Simplify).ToList();
            _bySignature[simple + "(" + string.Join(",", parts) + ")"] = summary;

            var arityKey = simple + "#" + parts.Count;
            if (_byArity.ContainsKey(arityKey))
            {
                _ambiguousArity.Add(arityKey);
            }
            else
            {
                _byArity[arityKey] = summary;
            }
        }

        /// <summary>
        /// A type id keeps its own simple name; a member id becomes "Type.Member".
        /// Generic arity suffixes are dropped on both sides, because reflection prints
        /// the argument names instead.
        /// </summary>
        private static string? SimplifyMemberName(string fullName, bool isType)
        {
            var segments = fullName.Split('.');
            if (segments.Length == 0)
            {
                return null;
            }

            var last = StripArity(segments[segments.Length - 1]);
            if (isType || segments.Length == 1)
            {
                return last;
            }

            return StripArity(segments[segments.Length - 2]) + "." + last;
        }

        private static string StripArity(string name)
        {
            var tick = name.IndexOf('`');
            return tick < 0 ? name : name.Substring(0, tick);
        }

        /// <summary>
        /// Spells a parameter type the way <see cref="Simplify"/> spells the same type
        /// out of a doc-comment id, so the two can be compared. A generic parameter
        /// becomes empty on both sides: an id names it by position (<c>`0</c>) and
        /// reflection names it by name, and neither can be derived from the other.
        /// </summary>
        private static string Reflected(Type type)
        {
            if (type.IsGenericParameter)
            {
                return string.Empty;
            }

            if (type.IsByRef || type.IsArray)
            {
                var element = Reflected(type.GetElementType()!);
                return type.IsArray ? element + "[]" : element;
            }

            if (type.IsGenericType)
            {
                return StripArity(type.Name) + "<"
                    + string.Join(",", type.GetGenericArguments().Select(Reflected)) + ">";
            }

            return type.Name;
        }

        /// <summary>Reduces a doc-comment type reference to the simple name reflection prints.</summary>
        private static string Simplify(string type)
        {
            var text = type.Replace('{', '<').Replace('}', '>').TrimEnd('@');
            var builder = new StringBuilder();
            var token = new StringBuilder();

            void FlushToken()
            {
                var value = token.ToString();
                var dot = value.LastIndexOf('.');
                builder.Append(StripArity(dot < 0 ? value : value.Substring(dot + 1)));
                token.Clear();
            }

            foreach (var c in text)
            {
                if (c == '<' || c == '>' || c == ',')
                {
                    FlushToken();
                    builder.Append(c);
                }
                else
                {
                    token.Append(c);
                }
            }

            FlushToken();
            return builder.ToString().Replace("[]", "[]");
        }

        /// <summary>Splits a doc-comment argument list, respecting nested generics.</summary>
        private static IEnumerable<string> SplitArguments(string arguments)
        {
            var depth = 0;
            var start = 0;
            for (var i = 0; i < arguments.Length; i++)
            {
                switch (arguments[i])
                {
                    case '{':
                        depth++;
                        break;
                    case '}':
                        depth--;
                        break;
                    case ',' when depth == 0:
                        yield return arguments.Substring(start, i - start);
                        start = i + 1;
                        break;
                }
            }

            if (arguments.Length > 0)
            {
                yield return arguments.Substring(start);
            }
        }

        /// <summary>
        /// Renders a summary element as one line of prose: <c>see</c>, <c>c</c> and
        /// <c>paramref</c> become code spans and everything else is collapsed.
        /// </summary>
        private static string Flatten(XElement summary)
        {
            var builder = new StringBuilder();
            foreach (var node in summary.DescendantNodes())
            {
                switch (node)
                {
                    case XText text when text.Parent == summary || text.Parent?.Name == "para":
                        builder.Append(text.Value);
                        break;
                    case XElement element:
                        builder.Append(Inline(element));
                        break;
                }
            }

            return string.Join(" ", builder.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        }

        private static string Inline(XElement element)
        {
            switch (element.Name.LocalName)
            {
                case "see":
                case "seealso":
                    var reference = (string?)element.Attribute("cref") ?? (string?)element.Attribute("langword");
                    return reference == null ? string.Empty : " `" + LastSegment(reference) + "` ";
                case "paramref":
                case "typeparamref":
                    return " `" + ((string?)element.Attribute("name") ?? string.Empty) + "` ";
                case "c":
                    return " `" + element.Value + "` ";
                case "em":
                case "b":
                    return " " + element.Value + " ";
                default:
                    return string.Empty;
            }
        }

        private static string LastSegment(string reference)
        {
            var body = reference.Length > 2 && reference[1] == ':' ? reference.Substring(2) : reference;
            var open = body.IndexOf('(');
            if (open >= 0)
            {
                body = body.Substring(0, open);
            }

            var dot = body.LastIndexOf('.');
            return StripArity(dot < 0 ? body : body.Substring(dot + 1));
        }
    }
}
