using System.Net.Http.Headers;
using Serilog;

namespace VRCVideoCacher.API;

public static class RemoteServerProxy
{
    private static readonly ILogger Log = Program.Logger.ForContext(typeof(RemoteServerProxy));
    private static readonly HttpClient HttpClient = new();

    public static bool IsEnabled =>
        ConfigManager.Config.RemoteServerEnabled &&
        ConfigManager.Config.RemoteServerUrls.Length > 0;

    public static IEnumerable<string> GetServers()
    {
        return ConfigManager.Config.RemoteServerUrls
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select(url => url.Trim().TrimEnd('/'));
    }

    private static void ConfigureTimeout()
    {
        var seconds = ConfigManager.Config.RemoteServerTimeoutSeconds;
        if (seconds <= 0)
            seconds = 15;
        HttpClient.Timeout = TimeSpan.FromSeconds(seconds);
    }

    public static async Task<(bool Success, int StatusCode, string Body)> GetVideo(
        string requestUrl,
        bool avPro,
        string source)
    {
        ConfigureTimeout();

        var lastStatus = 502;
        var lastBody = "Remote server unavailable.";
        foreach (var baseUrl in GetServers())
        {
            var url = $"{baseUrl}/api/getvideo?url={Uri.EscapeDataString(requestUrl)}&avpro={avPro}&source={source}";
            try
            {
                using var response = await HttpClient.GetAsync(url);
                var body = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode)
                {
                    Log.Information("Remote server responded: {Server}", baseUrl);
                    return (true, (int)response.StatusCode, body);
                }

                lastStatus = (int)response.StatusCode;
                lastBody = body;
                Log.Warning("Remote server error: {Server} {Status}", baseUrl, response.StatusCode);
            }
            catch (Exception ex)
            {
                lastStatus = 502;
                lastBody = ex.Message;
                Log.Warning("Remote server failed: {Server} {Error}", baseUrl, ex.Message);
            }
        }

        return (false, lastStatus, lastBody);
    }

    public static async Task<(bool Success, int StatusCode, string Body)> SendCookies(string cookies)
    {
        ConfigureTimeout();

        var lastStatus = 502;
        var lastBody = "Remote server unavailable.";
        foreach (var baseUrl in GetServers())
        {
            var url = $"{baseUrl}/api/youtube-cookies";
            try
            {
                using var content = new StringContent(cookies);
                content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
                using var response = await HttpClient.PostAsync(url, content);
                var body = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode)
                {
                    Log.Information("Remote server accepted cookies: {Server}", baseUrl);
                    return (true, (int)response.StatusCode, body);
                }

                lastStatus = (int)response.StatusCode;
                lastBody = body;
                Log.Warning("Remote server cookie error: {Server} {Status}", baseUrl, response.StatusCode);
            }
            catch (Exception ex)
            {
                lastStatus = 502;
                lastBody = ex.Message;
                Log.Warning("Remote server cookie failed: {Server} {Error}", baseUrl, ex.Message);
            }
        }

        return (false, lastStatus, lastBody);
    }
}
