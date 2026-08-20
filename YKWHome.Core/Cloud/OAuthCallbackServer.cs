using System.Net;
using System.Text;

namespace YKWHome.Core.Cloud;

/// <summary>
/// Local HTTP server that listens for OAuth callback on localhost.
/// Captures userId + secret from Appwrite redirect and passes them to the caller.
/// </summary>
public class OAuthCallbackServer : IDisposable
{
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private readonly TaskCompletionSource<(string userId, string secret)> _tcs = new();

    public int Port { get; }
    public string CallbackUrl => $"http://localhost:{Port}/callback";

    public OAuthCallbackServer(int port = 5287)
    {
        Port = port;
    }

    /// <summary>
    /// Start listening and return the callback URL.
    /// </summary>
    public async Task<(string userId, string secret)> WaitForCallbackAsync(TimeSpan? timeout = null)
    {
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{Port}/");
        _listener.Start();

        // Handle timeout
        var timeoutMs = (int)(timeout?.TotalMilliseconds ?? 120_000);
        var timer = new CancellationTokenSource(timeoutMs);
        timer.Token.Register(() => _tcs.TrySetCanceled());

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleRequest(context));
            }
        }
        catch (ObjectDisposedException) { }
        catch (HttpListenerException) { }

        return await _tcs.Task;
    }

    private void HandleRequest(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        // Parse the callback URL
        var url = request.Url?.ToString() ?? "";
        var query = request.QueryString;

        string? userId = query["userId"];
        string? secret = query["secret"];
        string? error = query["error"];

        // Send a response to the browser
        string html;
        if (error != null)
        {
            html = @"<!DOCTYPE html><html><head><title>YKW Home</title>
<style>body{font-family:sans-serif;display:flex;justify-content:center;align-items:center;height:100vh;
background:#1e1e2e;color:#cdd6f4;margin:0;}.card{text-align:center;padding:40px;border-radius:16px;
background:#313244;max-width:400px;}h2{color:#f38ba8;margin-bottom:8px;}p{color:#a6adc8;font-size:14px;}
</style></head><body><div class='card'><h2>❌ Auth Failed</h2><p>" + (error ?? "Unknown error") +
            @"</p><p>You can close this tab.</p></div></body></html>";
            response.StatusCode = 400;
        }
        else if (userId != null && secret != null)
        {
            html = @"<!DOCTYPE html><html><head><title>YKW Home</title>
<style>body{font-family:sans-serif;display:flex;justify-content:center;align-items:center;height:100vh;
background:#1e1e2e;color:#cdd6f4;margin:0;}.card{text-align:center;padding:40px;border-radius:16px;
background:#313244;max-width:400px;}h2{color:#a6e3a1;margin-bottom:8px;}p{color:#a6adc8;font-size:14px;}
</style></head><body><div class='card'><h2>✅ Login Successful!</h2>
<p>You can close this tab and return to YKW Home.</p></div></body></html>";
            response.StatusCode = 200;
            _tcs.TrySetResult((userId, secret));
        }
        else
        {
            html = @"<!DOCTYPE html><html><head><title>YKW Home</title>
<style>body{font-family:sans-serif;display:flex;justify-content:center;align-items:center;height:100vh;
background:#1e1e2e;color:#cdd6f4;margin:0;}.card{text-align:center;padding:40px;border-radius:16px;
background:#313244;max-width:400px;}p{color:#a6adc8;font-size:14px;}
</style></head><body><div class='card'><p>Waiting for OAuth callback...</p></div></body></html>";
            response.StatusCode = 200;
        }

        var buffer = Encoding.UTF8.GetBytes(html);
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = buffer.Length;
        response.OutputStream.Write(buffer, 0, buffer.Length);
        response.Close();
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _listener?.Stop(); } catch { }
        try { _listener?.Close(); } catch { }
    }

    public void Dispose()
    {
        Stop();
        _listener?.Close();
        _cts?.Dispose();
    }
}
