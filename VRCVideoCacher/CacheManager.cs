using System.Collections.Concurrent;
using Serilog;
using VRCVideoCacher.Models;

namespace VRCVideoCacher;

public class CacheManager
{
    private static readonly ILogger Log = Program.Logger.ForContext<CacheManager>();
    private static readonly ConcurrentDictionary<string, VideoCache> CachedAssets = new();
    private static CancellationTokenSource? _evictCts;
    public static readonly string CachePath;

    static CacheManager()
    {
        if (string.IsNullOrEmpty(ConfigManager.Config.CachedAssetPath))
            CachePath = Path.Combine(GetCacheFolder(), "CachedAssets");
        else if (Path.IsPathRooted(ConfigManager.Config.CachedAssetPath))
            CachePath = ConfigManager.Config.CachedAssetPath;
        else
            CachePath = Path.Combine(Program.CurrentProcessPath, ConfigManager.Config.CachedAssetPath);
        
        Log.Debug("Using cache path {CachePath}", CachePath);
        BuildCache();
    }

    private static string GetCacheFolder()
    {
        if (OperatingSystem.IsWindows())
            return Program.CurrentProcessPath;

        var cachePath = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        if (string.IsNullOrEmpty(cachePath))
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");
        
        return Path.Combine(cachePath, "VRCVideoCacher");
    }
    
    public static void Init()
    {
        TryFlushCache();
        StartEvictionThread();
    }
    
    private static void BuildCache()
    {
        CachedAssets.Clear();
        Directory.CreateDirectory(CachePath);
        var files = Directory.GetFiles(CachePath);
        foreach (var path in files)
        {
            var file = Path.GetFileName(path);
            AddToCache(file);
        }
    }
    
    private static void TryFlushCache()
    {
        if (ConfigManager.Config.CacheMaxSizeInGb <= 0f)
            return;
        
        var maxCacheSize = (long)(ConfigManager.Config.CacheMaxSizeInGb * 1024f * 1024f * 1024f);
        var cacheSize = GetCacheSize();
        if (cacheSize < maxCacheSize)
            return;

        var oldestFiles = CachedAssets.OrderBy(x => x.Value.LastModified).ToList();
        while (cacheSize >= maxCacheSize && oldestFiles.Count > 0)
        {
            var oldestFile = oldestFiles.First();
            var filePath = Path.Combine(CachePath, oldestFile.Value.FileName);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                cacheSize -= oldestFile.Value.Size;
            }
            CachedAssets.TryRemove(oldestFile.Key, out _);
            oldestFiles.RemoveAt(0);
        }
    }

    public static void AddToCache(string fileName)
    {
        var filePath = Path.Combine(CachePath, fileName);
        if (!File.Exists(filePath))
            return;
        
        var fileInfo = new FileInfo(filePath);
        var videoCache = new VideoCache
        {
            FileName = fileName,
            Size = fileInfo.Length,
            LastModified = fileInfo.LastWriteTimeUtc
        };
        
        var existingCache = CachedAssets.GetOrAdd(videoCache.FileName, videoCache);
        existingCache.Size = fileInfo.Length;
        existingCache.LastModified = fileInfo.LastWriteTimeUtc;
        
        TryFlushCache();
    }

    private static void StartEvictionThread()
    {
        var intervalMinutes = ConfigManager.Config.CacheEvictIntervalMinutes;
        var idleMinutes = ConfigManager.Config.CacheEvictUnusedMinutes;
        if (intervalMinutes <= 0 || idleMinutes <= 0)
            return;

        _evictCts?.Cancel();
        _evictCts = new CancellationTokenSource();
        var token = _evictCts.Token;
        var interval = TimeSpan.FromMinutes(intervalMinutes);
        var maxIdle = TimeSpan.FromMinutes(idleMinutes);

        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(interval, token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }

                try
                {
                    EvictUnused(maxIdle);
                }
                catch (Exception ex)
                {
                    Log.Warning("Failed to evict unused cache: {Error}", ex.Message);
                }
            }
        }, token);
    }

    private static void EvictUnused(TimeSpan maxIdle)
    {
        var cutoff = DateTime.UtcNow - maxIdle;
        foreach (var cache in CachedAssets.ToArray())
        {
            if (cache.Value.LastModified >= cutoff)
                continue;

            var filePath = Path.Combine(CachePath, cache.Value.FileName);
            if (File.Exists(filePath))
                File.Delete(filePath);
            CachedAssets.TryRemove(cache.Key, out _);
        }
    }
    
    private static long GetCacheSize()
    {
        var totalSize = 0L;
        foreach (var cache in CachedAssets)
        {
            totalSize += cache.Value.Size;
        }
        
        return totalSize;
    }
}
