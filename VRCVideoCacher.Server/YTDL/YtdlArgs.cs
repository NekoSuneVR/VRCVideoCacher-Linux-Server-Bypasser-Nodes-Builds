namespace VRCVideoCacher.YTDL;

public static class YtdlArgs
{
    public static string GetPoTokenArgs()
    {
        var baseUrl = ConfigManager.Config.YouTubePoTokenUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
            return string.Empty;

        var additionalArgs = ConfigManager.Config.ytdlAdditionalArgs;
        if (!string.IsNullOrEmpty(additionalArgs) &&
            additionalArgs.Contains("youtubepot-bgutilhttp:base_url=", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return $"--extractor-args youtubepot-bgutilhttp:base_url={baseUrl}";
    }
}
