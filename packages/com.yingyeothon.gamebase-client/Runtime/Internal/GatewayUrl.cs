using System;
using System.Collections.Generic;
using System.Text;

namespace Yingyeothon.Gamebase.Client
{
    /// <summary>Builds the gateway's WebSocket URL.</summary>
    public static class GatewayUrl
    {
        /// <summary>
        /// Produces <c>{url}?channel={channelId}[&amp;gameId={gameId}]</c>, keeping any
        /// query string already on <paramref name="url"/>.
        /// </summary>
        /// <remarks>
        /// This is <c>URLSearchParams.set</c>, not an append: a <c>channel</c> already
        /// present is replaced rather than duplicated, because the gateway reads only
        /// one and a duplicate would silently pick the wrong channel.
        /// </remarks>
        public static string Build(string url, string channelId, string? gameId = null)
        {
            if (url == null)
            {
                throw new ArgumentNullException(nameof(url));
            }

            if (channelId == null)
            {
                throw new ArgumentNullException(nameof(channelId));
            }

            var builder = new UriBuilder(url);
            var parameters = ParseQuery(builder.Query);
            Set(parameters, "channel", channelId);
            if (gameId != null)
            {
                Set(parameters, "gameId", gameId);
            }

            builder.Query = Format(parameters);
            // AbsoluteUri keeps percent-escapes; ToString() unescapes them for display and
            // would put a raw space into the handshake URL.
            return builder.Uri.AbsoluteUri;
        }

        private static List<KeyValuePair<string, string>> ParseQuery(string query)
        {
            var parameters = new List<KeyValuePair<string, string>>();
            var text = query.TrimStart('?');
            if (text.Length == 0)
            {
                return parameters;
            }

            foreach (var pair in text.Split('&'))
            {
                if (pair.Length == 0)
                {
                    continue;
                }

                var split = pair.IndexOf('=');
                var name = split < 0 ? pair : pair.Substring(0, split);
                var value = split < 0 ? string.Empty : pair.Substring(split + 1);
                parameters.Add(new KeyValuePair<string, string>(Uri.UnescapeDataString(name), Uri.UnescapeDataString(value)));
            }

            return parameters;
        }

        private static void Set(List<KeyValuePair<string, string>> parameters, string name, string value)
        {
            var first = -1;
            for (var i = 0; i < parameters.Count; i++)
            {
                if (string.Equals(parameters[i].Key, name, StringComparison.Ordinal))
                {
                    first = i;
                    break;
                }
            }

            if (first < 0)
            {
                parameters.Add(new KeyValuePair<string, string>(name, value));
                return;
            }

            // URLSearchParams.set replaces the first occurrence in place and drops
            // every later one, so the parameter keeps its original position.
            parameters[first] = new KeyValuePair<string, string>(name, value);
            for (var i = parameters.Count - 1; i > first; i--)
            {
                if (string.Equals(parameters[i].Key, name, StringComparison.Ordinal))
                {
                    parameters.RemoveAt(i);
                }
            }
        }

        private static string Format(List<KeyValuePair<string, string>> parameters)
        {
            var builder = new StringBuilder();
            foreach (var parameter in parameters)
            {
                if (builder.Length > 0)
                {
                    builder.Append('&');
                }

                builder.Append(Uri.EscapeDataString(parameter.Key));
                builder.Append('=');
                builder.Append(Uri.EscapeDataString(parameter.Value));
            }

            return builder.ToString();
        }
    }
}
