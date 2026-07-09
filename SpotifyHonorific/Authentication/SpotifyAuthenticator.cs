using Dalamud.Plugin.Services;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Auth;
using SpotifyHonorific.Utils;
using System;
using System.Net;
using System.Text;
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
        if (string.IsNullOrWhiteSpace(_config.SpotifyClientId))
        {
            _pluginLog.Error("Spotify authentication failed: Client ID is required.");
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
                Scope = [Scopes.UserReadCurrentlyPlaying, Scopes.UserReadPlaybackState, Scopes.UserModifyPlaybackState]
            };

            BrowserUtil.Open(loginRequest.ToUri());

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(AUTH_TIMEOUT_MINUTES));
            var context = await _authServer.ReceiveContext(timeoutCts.Token).ConfigureAwait(false);

            var code = context.Request.QueryString["code"];
            if (string.IsNullOrEmpty(code))
            {
                RespondToBrowser(context, 400,
                    "<html><body><h2>Spotify login failed</h2><p>No authorization code received. Close this tab and try again in the game.</p></body></html>");
                _pluginLog.Error("Spotify auth failed: No code received.");
                return false;
            }

            RespondToBrowser(context, 200,
                "<html><body><h2>Spotify login received</h2><p>You can close this tab and return to the game.</p></body></html>");

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

    private void RespondToBrowser(HttpListenerContext context, int statusCode, string html)
    {
        try
        {
            var buffer = Encoding.UTF8.GetBytes(html);
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = buffer.Length;
            context.Response.OutputStream.Write(buffer, 0, buffer.Length);
            context.Response.Close();
        }
        catch (Exception e)
        {
            // The browser may have gone away; a failed response page must
            // not fail the authentication itself.
            _pluginLog.Warning(e, "Could not write OAuth callback response to the browser.");
        }
    }

    public void Dispose()
    {
        _authServer?.Dispose();
        _authServer = null;
    }
}
