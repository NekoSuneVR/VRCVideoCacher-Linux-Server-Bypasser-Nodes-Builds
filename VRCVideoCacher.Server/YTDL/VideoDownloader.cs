using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text;
using Serilog;
using VRCVideoCacher.Models;

namespace VRCVideoCacher.YTDL;

public class VideoDownloader
{
    private static readonly ILogger Log = Program.Logger.ForContext<VideoDownloader>();
    private static readonly HttpClient HttpClient = new()
    {
        DefaultRequestHeaders = { { "User-Agent", "VRCVideoCacher" } }
    };
    private static readonly ConcurrentQueue<VideoInfo> DownloadQueue = new();
    private static readonly SemaphoreSlim DownloadLock = new(1, 1);
    private static readonly string TempDownloadMp4Path;
    private static readonly string TempDownloadWebmPath;
    
    static VideoDownloader()
    {
        TempDownloadMp4Path = Path.Combine(CacheManager.CachePath, "_tempVideo.mp4");
        TempDownloadWebmPath = Path.Combine(CacheManager.CachePath, "_tempVideo.webm");
        Task.Run(DownloadThread);
    }

    private static async Task DownloadThread()
    {
        while (true)
        {
            await Task.Delay(100);
            if (DownloadQueue.IsEmpty)
                continue;

            DownloadQueue.TryPeek(out var queueItem);
            if (queueItem == null)
                continue;
            
            switch (queueItem.UrlType)
            {
                case UrlType.YouTube:
                    if (ConfigManager.Config.CacheYouTube)
                        await DownloadYouTubeVideoInternal(queueItem);
                    break;
                case UrlType.PyPyDance:
                    if (ConfigManager.Config.CachePyPyDance)
                        await DownloadVideoWithId(queueItem);
                    break;
                case UrlType.VRDancing:
                    if (ConfigManager.Config.CacheVRDancing)
                        await DownloadVideoWithId(queueItem);
                    break;
                case UrlType.Other:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            DownloadQueue.TryDequeue(out _);
        }
    }
    
    public static void QueueDownload(VideoInfo videoInfo)
    {
        if (DownloadQueue.Any(x => x.VideoId == videoInfo.VideoId &&
                                   x.DownloadFormat == videoInfo.DownloadFormat))
        {
            return;
        }
        DownloadQueue.Enqueue(videoInfo);
    }

    public static async Task<string?> DownloadYouTubeVideoNow(VideoInfo videoInfo)
    {
        return await DownloadYouTubeVideoInternal(videoInfo);
    }

    private static async Task<string?> DownloadYouTubeVideoInternal(VideoInfo videoInfo)
    {
        if (string.IsNullOrEmpty(YtdlManager.YtdlPath))
        {
            Log.Error("yt-dlp not found. Set ytdlPath in Config.json or install yt-dlp in PATH.");
            return null;
        }

        await DownloadLock.WaitAsync();
        try
        {
            var url = videoInfo.VideoUrl;
            string? videoId;
            try
            {
                videoId = await VideoId.TryGetYouTubeVideoId(url);
            }
            catch (Exception ex)
            {
                Log.Error("Not downloading YouTube video: {URL} {ex}", url, ex.Message);
                return null;
            }

            var fileName = $"{videoId}.{videoInfo.DownloadFormat.ToString().ToLower()}";
            var filePath = Path.Combine(CacheManager.CachePath, fileName);
            if (File.Exists(filePath))
            {
                CacheManager.AddToCache(fileName);
                Log.Information("YouTube Video already cached: {URL}", $"{ConfigManager.Config.ytdlWebServerURL}/{fileName}");
                return fileName;
            }

            if (File.Exists(TempDownloadMp4Path))
            {
                Log.Error("Temp file already exists, deleting...");
                File.Delete(TempDownloadMp4Path);
            }
            if (File.Exists(TempDownloadWebmPath))
            {
                Log.Error("Temp file already exists, deleting...");
                File.Delete(TempDownloadWebmPath);
            }

            Log.Information("Downloading YouTube Video: {URL}", url);

            var additionalArgs = ConfigManager.Config.ytdlAdditionalArgs;
            var potArgs = YtdlArgs.GetPoTokenArgs();
            var cookieArg = string.Empty;
            if (Program.IsCookiesEnabledAndValid())
                cookieArg = $"--cookies \"{YtdlManager.CookiesPath}\"";
        
            var audioArg = string.IsNullOrEmpty(ConfigManager.Config.ytdlDubLanguage)
                ? "+ba[acodec=opus][ext=webm]"
                : $"+(ba[acodec=opus][ext=webm][language={ConfigManager.Config.ytdlDubLanguage}]/ba[acodec=opus][ext=webm])";
        
        var audioArgPotato = string.IsNullOrEmpty(ConfigManager.Config.ytdlDubLanguage)
            ? "+ba[ext=m4a]"
            : $"+(ba[ext=m4a][language={ConfigManager.Config.ytdlDubLanguage}]/ba[ext=m4a])";

        async Task<(int ExitCode, string Error)> RunDownloadAsync(string args)
        {
            var process = new Process
            {
                StartInfo =
                {
                    FileName = YtdlManager.YtdlPath,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                    Arguments = args
                }
            };
            process.Start();
            await process.WaitForExitAsync();
            var err = await process.StandardError.ReadToEndAsync();
            return (process.ExitCode, err.Trim());
        }

        string primaryArgs;
        string fallbackArgs;
        if (videoInfo.DownloadFormat == DownloadFormat.Webm)
        {
            primaryArgs = $"--encoding utf-8 -q -o \"{TempDownloadWebmPath}\" -f \"bv*[height<={ConfigManager.Config.CacheYouTubeMaxResolution}][vcodec~='^av01'][ext=mp4][dynamic_range='SDR']{audioArg}/bv*[height<={ConfigManager.Config.CacheYouTubeMaxResolution}][vcodec~='vp9'][ext=webm][dynamic_range='SDR']{audioArg}\" --no-mtime --no-playlist --no-progress {potArgs} {cookieArg} {additionalArgs} -- \"{videoId}\"";
            fallbackArgs = $"--encoding utf-8 -q -o \"{TempDownloadWebmPath}\" -f \"bestvideo[ext=webm]+bestaudio[ext=webm]/best[ext=webm]/best\" --merge-output-format webm --no-mtime --no-playlist --no-progress {potArgs} {cookieArg} {additionalArgs} -- \"{videoId}\"";
        }
        else
        {
            primaryArgs = $"--encoding utf-8 -q -o \"{TempDownloadMp4Path}\" -f \"bv*[height<=1080][vcodec~='^(avc|h264)']{audioArgPotato}/bv*[height<=1080][vcodec~='^av01'][dynamic_range='SDR']\" --no-mtime --no-playlist --remux-video mp4 --no-progress {potArgs} {cookieArg} {additionalArgs} -- \"{videoId}\"";
            fallbackArgs = $"--encoding utf-8 -q -o \"{TempDownloadMp4Path}\" -f \"bestvideo[ext=mp4]+bestaudio[ext=m4a]/best[ext=mp4]/best\" --merge-output-format mp4 --no-mtime --no-playlist --no-progress {potArgs} {cookieArg} {additionalArgs} -- \"{videoId}\"";
        }

        var (exitCode, error) = await RunDownloadAsync(primaryArgs);
        if (exitCode != 0 && error.Contains("Requested format is not available", StringComparison.OrdinalIgnoreCase))
        {
            Log.Warning("Requested format not available, retrying with fallback.");
            (exitCode, error) = await RunDownloadAsync(fallbackArgs);
        }
        if (exitCode != 0)
        {
            Log.Error("Failed to download YouTube Video: {exitCode} {URL} {error}", exitCode, url, error);
            if (error.Contains("Sign in to confirm", StringComparison.OrdinalIgnoreCase))
                Log.Error("Fix this error by following these instructions: https://github.com/clienthax/VRCVideoCacherBrowserExtension");
            
            return null;
        }
            Thread.Sleep(100);
        
            if (File.Exists(filePath))
            {
                Log.Error("File already exists, canceling...");
                try
                {
                    if (File.Exists(TempDownloadMp4Path))
                        File.Delete(TempDownloadMp4Path);
                    if (File.Exists(TempDownloadWebmPath))
                        File.Delete(TempDownloadWebmPath);
                }
                catch (Exception ex)
                {
                    Log.Error("Failed to delete temp file: {ex}", ex.Message);
                }
                return fileName;
            }
        
            if (File.Exists(TempDownloadMp4Path))
            {
                File.Move(TempDownloadMp4Path, filePath);
            }
            else if (File.Exists(TempDownloadWebmPath))
            {
                File.Move(TempDownloadWebmPath, filePath);
            }
            else
            {
                Log.Error("Failed to download YouTube Video: {URL}", url);
                return null;
            }

            CacheManager.AddToCache(fileName);
            Log.Information("YouTube Video Downloaded: {URL}", $"{ConfigManager.Config.ytdlWebServerURL}/{fileName}");
            return fileName;
        }
        finally
        {
            DownloadLock.Release();
        }
    }
    
    private static async Task DownloadVideoWithId(VideoInfo videoInfo)
    {
        await DownloadLock.WaitAsync();
        try
        {
            if (File.Exists(TempDownloadMp4Path))
            {
                Log.Error("Temp file already exists, deleting...");
                File.Delete(TempDownloadMp4Path);
            }
            if (File.Exists(TempDownloadWebmPath))
            {
                Log.Error("Temp file already exists, deleting...");
                File.Delete(TempDownloadWebmPath);
            }

            Log.Information("Downloading Video: {URL}", videoInfo.VideoUrl);
            var url = videoInfo.VideoUrl;
            var response = await HttpClient.GetAsync(url);
            if (response.StatusCode == HttpStatusCode.Redirect)
            {
                Log.Information("Redirected to: {URL}", response.Headers.Location);
                url = response.Headers.Location?.ToString();
                response = await HttpClient.GetAsync(url);
            }
            if (!response.IsSuccessStatusCode)
            {
                Log.Error("Failed to download video: {URL}", url);
                return;
            }

            await using var stream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = new FileStream(TempDownloadMp4Path, FileMode.Create, FileAccess.Write, FileShare.None);
            await stream.CopyToAsync(fileStream);
            fileStream.Close();
            await Task.Delay(10);
        
            var fileName = $"{videoInfo.VideoId}.{videoInfo.DownloadFormat.ToString().ToLower()}";
            var filePath = Path.Combine(CacheManager.CachePath, fileName);
            if (File.Exists(TempDownloadMp4Path))
            {
                File.Move(TempDownloadMp4Path, filePath);
            }
            else if (File.Exists(TempDownloadWebmPath))
            {
                File.Move(TempDownloadWebmPath, filePath);
            }
            else
            {
                Log.Error("Failed to download Video: {URL}", url);
                return;
            }
            Log.Information("Video Downloaded: {URL}", $"{ConfigManager.Config.ytdlWebServerURL}/{fileName}");
        }
        finally
        {
            DownloadLock.Release();
        }
    }
}
