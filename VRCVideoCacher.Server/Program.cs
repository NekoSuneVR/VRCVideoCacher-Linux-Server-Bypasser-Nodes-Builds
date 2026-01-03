using Serilog;
using Serilog.Templates;
using Serilog.Templates.Themes;
using VRCVideoCacher.API;
using VRCVideoCacher.YTDL;

namespace VRCVideoCacher;

internal static class Program
{
    public const string Version = "2025.11.24";
    public static readonly ILogger Logger = Log.ForContext("SourceContext", "Core");
    public static readonly string CurrentProcessPath = Path.GetDirectoryName(Environment.ProcessPath) ?? string.Empty;
    public static readonly string DataPath = CurrentProcessPath;

    public static async Task Main(string[] args)
    {
        Console.Title = $"VRCVideoCacher v{Version}";
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(new ExpressionTemplate(
                "[{@t:HH:mm:ss} {@l:u3} {Coalesce(Substring(SourceContext, LastIndexOf(SourceContext, '.') + 1),'<none>')}] {@m}\n{@x}",
                theme: TemplateTheme.Literate))
            .CreateLogger();
        const string elly = "Elly";
        const string natsumi = "Natsumi";
        const string haxy = "Haxy";
        Logger.Information("VRCVideoCacher version {Version} created by {Elly}, {Natsumi}, {Haxy}", Version, elly, natsumi, haxy);
        
        Directory.CreateDirectory(DataPath);
        if (Environment.CommandLine.Contains("--Reset"))
        {
            Environment.Exit(0);
        }
        Console.CancelKeyPress += (_, _) => Environment.Exit(0);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => OnAppQuit();

        var ytdlMissing = string.IsNullOrEmpty(YtdlManager.YtdlPath) || !File.Exists(YtdlManager.YtdlPath);
        if (ytdlMissing || ConfigManager.Config.ytdlAutoUpdate)
        {
            await YtdlManager.TryDownloadYtdlp();
            if (ConfigManager.Config.ytdlAutoUpdate)
            {
                YtdlManager.StartYtdlDownloadThread();
                _ = YtdlManager.TryDownloadDeno();
                _ = YtdlManager.TryDownloadFfmpeg();
            }
        }

        WebServer.Init();
        await BulkPreCache.DownloadFileList();

        if (ConfigManager.Config.ytdlUseCookies && !IsCookiesEnabledAndValid())
            Logger.Warning("No cookies found, please use the browser extension to send cookies or disable \"ytdlUseCookies\" in config.");

        CacheManager.Init();

        if (YtdlManager.GlobalYtdlConfigExists())
            Logger.Error("Global yt-dlp config file found in \"%AppData%\\yt-dlp\". Please delete it to avoid conflicts with VRCVideoCacher.");
        
        await Task.Delay(-1);
    }

    public static bool IsCookiesEnabledAndValid()
    {
        if (!ConfigManager.Config.ytdlUseCookies)
            return false;

        if (!File.Exists(YtdlManager.CookiesPath))
            return false;
        
        var cookies = File.ReadAllText(YtdlManager.CookiesPath);
        return IsCookiesValid(cookies);
    }

    public static bool IsCookiesValid(string cookies)
    {
        if (string.IsNullOrEmpty(cookies))
            return false;

        if (cookies.Contains("youtube.com") && cookies.Contains("LOGIN_INFO"))
            return true;

        return false;
    }

    private static void OnAppQuit()
    {
        Logger.Information("Exiting...");
    }
}
