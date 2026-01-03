using EmbedIO;
using EmbedIO.Files;
using EmbedIO.WebApi;
using Swan.Logging;
using ILogger = Serilog.ILogger;
using System.Net;
using System.Net.NetworkInformation;

namespace VRCVideoCacher.API;

public class WebServer
{
    private static EmbedIO.WebServer? _server;
    public static readonly ILogger Log = Program.Logger.ForContext<WebServer>();
    
    public static void Init()
    {
        var indexPath = Path.Combine(CacheManager.CachePath, "index.html");
        if (!File.Exists(indexPath))
            File.WriteAllText(indexPath, "VRCVideoCacher");
        
        _server = CreateWebServer();
        _server.RunAsync();  
    }
    
    private static EmbedIO.WebServer CreateWebServer()
    {
        Logger.UnregisterLogger<ConsoleLogger>();
        Logger.RegisterLogger<WebServerLogger>();

        var urls = new List<string>();
        if (ConfigManager.Config.WebServerBindUrls.Length > 0)
        {
            urls.AddRange(ExpandBindUrls(ConfigManager.Config.WebServerBindUrls));
        }
        else
        {
            urls.AddRange(ExpandBindUrls(["http://localhost:9696", "http://127.0.0.1:9696"]));
        }
        
        var server = new EmbedIO.WebServer(o => o
                .WithUrlPrefixes(urls)
                .WithMode(HttpListenerMode.EmbedIO))
            // First, we will configure our web server by adding Modules.
            .WithWebApi("/api", m => m
                .WithController<ApiController>())
            .WithStaticFolder("/", CacheManager.CachePath, true, m => m
                .WithContentCaching(true));

        // Listen for state changes.
        server.StateChanged += (_, e) => $"WebServer State: {e.NewState}".Info();
        server.OnUnhandledException += OnUnhandledException;
        server.OnHttpException += OnHttpException;  
        return server;
    }

    private static IEnumerable<string> ExpandBindUrls(IEnumerable<string> bindUrls)
    {
        var urls = new List<string>();
        foreach (var url in bindUrls)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                urls.Add(url);
                continue;
            }

            if (uri.Host == "0.0.0.0")
            {
                urls.Add(url);
                foreach (var ip in GetHostIPv4s())
                {
                    var builder = new UriBuilder(uri) { Host = ip };
                    urls.Add(builder.Uri.ToString().TrimEnd('/'));
                }
            }
            else
            {
                urls.Add(url);
            }
        }
        return urls.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> GetHostIPv4s()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up)
                continue;

            var props = nic.GetIPProperties();
            foreach (var addr in props.UnicastAddresses)
            {
                if (addr.Address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                    continue;
                if (IPAddress.IsLoopback(addr.Address))
                    continue;
                yield return addr.Address.ToString();
            }
        }
    }

    private static Task OnHttpException(IHttpContext context, IHttpException httpException)
    {
        Log.Information(httpException.Message!);
        return Task.CompletedTask;
    }

    private static Task OnUnhandledException(IHttpContext context, Exception exception)
    {
        Log.Information(exception.Message);
        return Task.CompletedTask;
    }
}
