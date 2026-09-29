using System;

namespace Yingyeothon.Auth
{
    /// <summary>The <see cref="AuthException.Code"/> values, in the vocabulary flutterlib's auth client shares.</summary>
    public static class AuthErrorCodes
    {
        /// <summary>The service answered a status that is not a success; <see cref="AuthException.Status"/> carries it.</summary>
        public const string Http = "http";

        /// <summary>A success reply whose body was not a JSON object, or was larger than any auth answer.</summary>
        public const string NotJson = "not_json";

        /// <summary>A success reply without a field the call needs: <c>jwt</c>, <c>userId</c> or <c>exp</c>.</summary>
        public const string MissingField = "missing_field";

        /// <summary><see cref="IAuthClient.ParseRedirect"/>: the redirect's nonce is absent or not the one you sent.</summary>
        public const string NonceMismatch = "nonce_mismatch";

        /// <summary><see cref="IAuthClient.ParseRedirect"/>: the redirect carries no fragment, or no <c>token</c> in it.</summary>
        public const string MissingFragment = "missing_fragment";

        /// <summary>No reply arrived: the transport threw, or nothing came within <see cref="AuthClientOptions.Timeout"/>.</summary>
        public const string Network = "network";
    }

    /// <summary>An auth call that did not produce a token or a config.</summary>
    /// <remarks>
    /// The message is <c>"auth {code} ({status})"</c> and nothing else — never a response
    /// body, a token, a fragment or a URL — so it is safe to log as it is. A failed reply's
    /// body may quote the credential back, which is why only the status is kept.
    /// </remarks>
    public sealed class AuthException : Exception
    {
        /// <summary>Creates an exception for a code and a status.</summary>
        /// <param name="code">An <see cref="AuthErrorCodes"/> value.</param>
        /// <param name="status">The HTTP status, or 0 when there was no reply.</param>
        public AuthException(string code, int status)
            : this(code, status, null)
        {
        }

        /// <summary>Creates an exception carrying the transport failure behind a <see cref="AuthErrorCodes.Network"/>.</summary>
        public AuthException(string code, int status, Exception? innerException)
            : base("auth " + (code ?? throw new ArgumentNullException(nameof(code))) + " (" + status + ")", innerException)
        {
            Code = code;
            Status = status;
        }

        /// <summary>What went wrong; one of <see cref="AuthErrorCodes"/>.</summary>
        public string Code { get; }

        /// <summary>The HTTP status, or 0 when there was no reply to report.</summary>
        public int Status { get; }
    }
}
