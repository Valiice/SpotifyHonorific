using Dalamud.Plugin.Services;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Auth;
using SpotifyHonorific.Utils;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SpotifyHonorific.Authentication;

public class SpotifyAuthenticator : IDisposable
{
    private const int AUTH_TIMEOUT_MINUTES = 1;
    private static readonly Uri ServerUri = new("http://127.0.0.1:5000");

    private readonly Config _config;
    private readonly IPluginLog _pluginLog;
    private PKCECallbackActivator? _authServer;

    public SpotifyAuthenticator(Config config, IPluginLog pluginLog)
    {
        _config = config;
        _pluginLog = pluginLog;
    }

    /// <summary>
    /// Starts the OAuth PKCE authentication flow with Spotify.
    /// Opens a browser for user login and waits for callback.
    /// </summary>
    public async Task<bool> AuthenticateAsync()
    {
        if (string.IsNullOrWhiteSpace(_config.SpotifyClientId) ||
            string.IsNullOrWhiteSpace(_config.SpotifyClientSecret))
        {
            _pluginLog.Error("Spotify authentication failed: Client ID and Secret are required.");
            return false;
        }

        try
        {
            _authServer?.Dispose();
            _authServer = new PKCECallbackActivator(ServerUri, "callback");

            await _authServer.Start().ConfigureAwait(false);

            var (verifier, challenge) = PKCEUtil.GenerateCodes();
            var loginRequest = new LoginRequest(_authServer.RedirectUri, _config.SpotifyClientId, LoginRequest.ResponseType.Code)
            {
                CodeChallenge = challenge,
                CodeChallengeMethod = "S256",
                Scope = [Scopes.UserReadCurrentlyPlaying, Scopes.UserReadPlaybackState]
            };

            BrowserUtil.Open(loginRequest.ToUri());

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(AUTH_TIMEOUT_MINUTES));
            var context = await _authServer.ReceiveContext(timeoutCts.Token).ConfigureAwait(false);

            var code = context.Request.QueryString["code"];
            if (string.IsNullOrEmpty(code))
            {
                _pluginLog.Error("Spotify auth failed: No code received.");
                return false;
            }

            var tokenResponse = await new OAuthClient().RequestToken(
                new PKCETokenRequest(_config.SpotifyClientId, code, _authServer.RedirectUri, verifier)
            ).ConfigureAwait(false);

            _config.SpotifyRefreshToken = tokenResponse.RefreshToken;
            _config.LastSpotifyAuthTime = DateTime.Now;
            _config.Save();

            _pluginLog.Information("Successfully authenticated with Spotify!");
            return true;
        }
        catch (OperationCanceledException)
        {
            _pluginLog.Warning("Spotify authentication timed out.");
            return false;
        }
        catch (Exception e)
        {
            _pluginLog.Error(e, "Spotify authentication failed");
            return false;
        }
        finally
        {
            _authServer?.Dispose();
            _authServer = null;
        }
    }

    public void Dispose()
    {
        _authServer?.Dispose();
        _authServer = null;
    }
}
