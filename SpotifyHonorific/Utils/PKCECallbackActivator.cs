using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace SpotifyHonorific.Utils;

// Source: https://github.com/JohnnyCrazy/SpotifyAPI-NET/blob/master/SpotifyAPI.Web.Auth/Example/PKCECallbackActivator.cs
public sealed class PKCECallbackActivator : IDisposable
{
    private readonly HttpListener _httpListener;
    private readonly CancellationTokenSource _cancelTokenSource;

    public Uri RedirectUri { get; }
    public string CallbackPath { get; }

    public PKCECallbackActivator(Uri serverUri, string callbackPath)
    {
        if (!callbackPath.StartsWith("/"))
        {
            callbackPath = $"/{callbackPath}";
        }

        RedirectUri = new Uri(serverUri, callbackPath);
        CallbackPath = callbackPath;

        _httpListener = new HttpListener();
        _httpListener.Prefixes.Add(serverUri.ToString());

        _cancelTokenSource = new CancellationTokenSource();
    }

    public Task Start()
    {
        if (_httpListener.IsListening)
        {
            return Task.CompletedTask;
        }

        try
        {
            _httpListener.Start();
        }
        catch (HttpListenerException ex)
        {
            throw new Exception("HTTP Listener could not be started", ex);
        }

        return Task.CompletedTask;
    }

    public void Stop()
    {
        if (_httpListener.IsListening)
        {
            _httpListener.Stop();
        }
    }

    public async Task<HttpListenerContext> ReceiveContext(CancellationToken cancelToken)
    {
        using var linkedToken = CancellationTokenSource.CreateLinkedTokenSource(
          _cancelTokenSource.Token, cancelToken
        );

        while (true)
        {
            var contextTask = _httpListener.GetContextAsync();
            var cancelTask = Task.Delay(Timeout.Infinite, linkedToken.Token);

            var resultTask = await Task.WhenAny(contextTask, cancelTask).ConfigureAwait(false);
            if (resultTask == cancelTask)
            {
                throw new TaskCanceledException();
            }

            var context = await contextTask.ConfigureAwait(false);
            if (context.Request.Url?.AbsolutePath == CallbackPath)
            {
                return context;
            }

            context.Response.StatusCode = 404;
            context.Response.Close();
        }
    }

    public void Dispose()
    {
        if (_httpListener.IsListening)
        {
            _httpListener.Stop();
        }
        _cancelTokenSource.Dispose();
    }
}
