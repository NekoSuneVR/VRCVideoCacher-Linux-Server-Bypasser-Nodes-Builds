using Newtonsoft.Json;
using Serilog;

// ReSharper disable FieldCanBeMadeReadOnly.Global

namespace VRCVideoCacher;

public class ConfigManager
{
    public static readonly ConfigModel Config;
    private static readonly ILogger Log = Program.Logger.ForContext<ConfigManager>();
    private static readonly string ConfigFilePath;
    public static readonly string UtilsPath;

    static ConfigManager()
    {
        Log.Information("Loading config...");
        ConfigFilePath = Path.Combine(Program.DataPath, "Config.json");
        Log.Debug("Using config file path: {ConfigFilePath}", ConfigFilePath);

        if (!File.Exists(ConfigFilePath))
        {
            Config = new ConfigModel();
            FirstRun();
        }
        else
        {
            Config = JsonConvert.DeserializeObject<ConfigModel>(File.ReadAllText(ConfigFilePath)) ?? new ConfigModel();
        }
        if (Config.ytdlWebServerURL.EndsWith('/'))
            Config.ytdlWebServerURL = Config.ytdlWebServerURL.TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(Config.YouTubePoTokenUrl))
            Config.YouTubePoTokenUrl = Config.YouTubePoTokenUrl.Trim().TrimEnd('/');
        Config.WebServerBindUrls ??= [];
        if (Config.WebServerBindUrls.Length > 0)
        {
            Config.WebServerBindUrls = Config.WebServerBindUrls
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Select(url => url.Trim().TrimEnd('/'))
                .ToArray();
        }

        UtilsPath = Path.GetDirectoryName(Config.ytdlPath) ?? string.Empty;
        if (!UtilsPath.EndsWith("Utils"))
            UtilsPath = Path.Combine(UtilsPath, "Utils");

        Directory.CreateDirectory(UtilsPath);
        
        Log.Information("Loaded config.");
        TrySaveConfig();
    }

    private static void TrySaveConfig()
    {
        var newConfig = JsonConvert.SerializeObject(Config, Formatting.Indented);
        var oldConfig = File.Exists(ConfigFilePath) ? File.ReadAllText(ConfigFilePath) : string.Empty;
        if (newConfig == oldConfig)
            return;
        
        Log.Information("Config changed, saving...");
        File.WriteAllText(ConfigFilePath, JsonConvert.SerializeObject(Config, Formatting.Indented));
        Log.Information("Config saved.");
    }
    
    private static void FirstRun()
    {
        Log.Information("First run detected, writing server defaults.");
        Config.CacheYouTube = true;
        Config.CacheYouTubeMaxResolution = 2160;
        Config.CacheVRDancing = false;
        Config.CachePyPyDance = false;
        Config.ytdlUseCookies = true;
        Config.PatchResonite = false;
        Config.PatchVRC = false;
    }
}

// ReSharper disable InconsistentNaming
public class ConfigModel
{
    public string ytdlWebServerURL = "http://localhost:9696";
    public string ytdlPath = "";
    public bool ytdlUseCookies = true;
    public bool ytdlAutoUpdate = true;
    public string ytdlAdditionalArgs = string.Empty;
    public string ytdlDubLanguage = string.Empty;
    public int ytdlDelay = 0;
    public string YouTubePoTokenUrl = "";
    public string CachedAssetPath = "";
    public string[] WebServerBindUrls = ["http://0.0.0.0:9696"];
    public string[] BlockedUrls = [];
    public string BlockRedirect = "https://www.youtube.com/watch?v=byv2bKekeWQ";
    public bool CacheYouTube = true;
    public int CacheYouTubeMaxResolution = 2160;
    public int CacheYouTubeMaxLength = 120;
    public float CacheMaxSizeInGb = 0;
    public int CacheEvictUnusedMinutes = 0;
    public int CacheEvictIntervalMinutes = 0;
    public bool CachePyPyDance = false;
    public bool CacheVRDancing = false;
    public bool PatchResonite = false;
    public bool PatchVRC = false;
    public bool AutoUpdate = true;
    public string[] PreCacheUrls = [];
    
}
// ReSharper restore InconsistentNaming
