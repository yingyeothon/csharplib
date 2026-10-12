using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Yingyeothon.Auth;
using Yingyeothon.Codec;

namespace Yingyeothon.GameKit
{
    /// <summary>Identity and platform time; token adoption reads claims, never verifies a signature.</summary>
    public interface IKitSession
    {
        /// <summary>Player JWT for wire clients. Never log it.</summary>
        string? Token { get; }
        /// <summary>Authenticated player identity claimed by the adopted token.</summary>
        string? UserId { get; }
        /// <summary>Token expiry in epoch seconds.</summary>
        long? ExpiresAt { get; }
        /// <summary>Opens the injected login flow and adopts its validated redirect.</summary>
        Task SignInAsync(CancellationToken cancellationToken = default);
        /// <summary>Adopts a JWT and synchronizes the platform clock.</summary>
        Task FromTokenAsync(string jwt, CancellationToken cancellationToken = default);
        /// <summary>Restores opted-in token storage.</summary>
        Task RestoreAsync(CancellationToken cancellationToken = default);
        /// <summary>Stops all modules and clears opted-in token storage.</summary>
        Task SignOutAsync(CancellationToken cancellationToken = default);
        /// <summary>Platform epoch milliseconds; unavailable before first successful synchronization.</summary>
        double Now();
        /// <summary>Raised once per adopted token at expiry minus ten minutes.</summary>
        event Action Expiring;
        /// <summary>Raised once when the active session ends.</summary>
        event Action SignedOut;
    }
    internal sealed class SessionImpl : IKitSession
    {
        private readonly KitRuntime _r;
        private readonly IKitTokenStorage? _storage;
        private readonly IKitLoginAdapter? _login;
        private readonly IAuthClient? _auth;
        private readonly Uri? _redirect;
        private readonly string _provider;
        private readonly SemaphoreSlim _storageLock = new SemaphoreSlim(1, 1);
        private bool _expiring;
        private double? _offset;
        internal SessionImpl(KitRuntime r, GameKitOptions o)
        {
            _r = r; _storage = o.TokenStorage; _login = o.LoginAdapter; _redirect = o.RedirectUrl; _provider = o.Provider ?? (r.Config.TryGetMember("auth", out var authConfig) ? authConfig.GetString("provider") : null) ?? "github";
            if (r.Config.TryGetMember("auth", out var auth) && auth.GetString("url") is string url && auth.GetString("channelId") is string channel)
            {
                _auth = AuthClient.Create(new AuthClientOptions { BaseUrl = url, ChannelId = channel, Transport = o.AuthTransport });
            }
        }
        public string? Token { get; private set; }
        public string? UserId { get; private set; }
        public long? ExpiresAt { get; private set; }
        public event Action? Expiring;
        public event Action? SignedOut;
        public double Now() => _offset.HasValue ? _r.Clock.NowMillis + _offset.Value : throw new KitException("session", "unavailable");
        public Task SignInAsync(CancellationToken ct = default) => _r.Run("session", async () =>
        {
            if (_auth == null || _login == null || _redirect == null)
            {
                throw new KitException("session", "not_configured");
            }

            var generation = _r.Generation;
            var nonce = AuthClient.NewNonce();
            var returned = await _login.OpenAsync(_auth.BuildStartUrl(_provider, _redirect, nonce), ct);
            if (generation != _r.Generation || _r.Disposed)
            {
                throw new KitException("session", "cancelled");
            }

            var token = _auth.ParseRedirect(returned, nonce);
            await Adopt(token.Jwt, ct);
        }, ct);
        public Task FromTokenAsync(string jwt, CancellationToken ct = default) => _r.Run("session", () => Adopt(jwt, ct), ct);
        private async Task Adopt(string jwt, CancellationToken ct)
        {
            JsonValue claims;
            try
            {
                if (jwt == null || jwt.Length > 16384)
                {
                    throw new ArgumentException("Invalid JWT");


                }

                var parts = jwt.Split('.'); if (parts.Length != 3)
                {
                    throw new ArgumentException("Invalid JWT");
                }

                var encoded = parts[1].Replace('-', '+').Replace('_', '/'); encoded += new string('=', (4 - encoded.Length % 4) % 4);
                claims = Json.Parse(new UTF8Encoding(false, true).GetString(Convert.FromBase64String(encoded)));
            }
            catch (Exception) { throw new ArgumentException("Expected a bounded JWT with JSON claims", nameof(jwt)); }
            var user = claims.GetString("sub"); var exp = claims.GetNumber("exp");
            if (string.IsNullOrEmpty(user) || exp == null || exp < 0 || exp > 253402300799L || exp != Math.Floor(exp.Value))
            {
                throw new ArgumentException("JWT identity and expiry required", nameof(jwt));
            }

            Stop(false); Token = jwt; UserId = user; ExpiresAt = (long)exp; _expiring = false; _offset = null;
            var generation = _r.Generation;
            if (_storage != null)
            {
                await _storageLock.WaitAsync(ct);
                try { _r.Check(generation); await _storage.StoreAsync(jwt, ct); }
                finally { _storageLock.Release(); }
                _r.Check(generation);
            }
            try { await Synchronize(generation, ct); }
            catch (Exception e) when (e is not OperationCanceledException && _r.QueueStorage != null) { _r.Check(generation); }
        }
        private async Task Synchronize(int generation, CancellationToken ct)
        {
            var start = _r.Clock.NowMillis; var epoch = await _r.Platform().TimeAsync(ct); var end = _r.Clock.NowMillis;
            _r.Check(generation); _offset = epoch - (start + end) / 2;
        }
        public Task RestoreAsync(CancellationToken ct = default) => _r.Run("session", async () =>
        {
            if (_storage == null)
            {
                return;
            }

            var generation = _r.Generation;
            var token = await _storage.LoadAsync(ct);
            if (generation != _r.Generation || _r.Disposed)
            {
                throw new KitException("session", "cancelled");
            }

            if (token != null)
            {
                await Adopt(token, ct);
            }
        }, ct);
        public Task SignOutAsync(CancellationToken ct = default) => _r.Run("session", async () =>
        {
            var intendedGeneration = _r.Generation;
            try
            {
                if (Token != null)
                {
                    await _r.Push.UnregisterAsync(ct);
                }
            }
            finally
            {
                if (intendedGeneration == _r.Generation)
                {
                    Stop();
                }

                var stoppedGeneration = _r.Generation;
                if (_storage != null && Token == null)
                {
                    await _storageLock.WaitAsync(CancellationToken.None);
                    try
                    {
                        if (stoppedGeneration == _r.Generation && Token == null)
                        {
                            await _storage.ClearAsync(CancellationToken.None);
                        }
                    }
                    finally { _storageLock.Release(); }
                }
            }
        }, ct);
        internal void Stop(bool notify = true)
        {
            var priorToken = Token; var wasActive = Token != null; Token = null; UserId = null; ExpiresAt = null; _offset = null; _r.Generation++;
            _r.StopClients(priorToken);
            if (wasActive && notify)
            {
                SignedOut?.Invoke();
            }
        }
        internal void Tick()
        {
            if (Token == null || !_offset.HasValue)
            {
                return;
            }

            var seconds = Now() / 1000;
            if (seconds >= ExpiresAt) { Stop(); return; }
            if (!_expiring && seconds >= ExpiresAt - 600) { _expiring = true; Expiring?.Invoke(); }
        }
        internal void Resume()
        {
            if (Token != null && !_offset.HasValue)
            {
                _ = _r.Run("session", () => Synchronize(_r.Generation, CancellationToken.None));
            }
        }
    }
}
