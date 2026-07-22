namespace PlayerBuffBar
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using GameHelper;

    internal sealed class BuffIconLoader
    {
        private static readonly HttpClient Http = CreateHttpClient();

        private readonly Dictionary<string, (IntPtr Ptr, int W, int H)> textures = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> pendingDownloads = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> failedDownloads = new(StringComparer.OrdinalIgnoreCase);
        private readonly object sync = new();

        private string iconsDirectory = string.Empty;
        private string pluginDirectory = string.Empty;
        private BuffIconCatalog.IconMapData iconMap;
        private int reloadRequested;
        private string lastLogLine = string.Empty;

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) PlayerBuffBar/1.2");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Referer", BuffIconCatalog.Poe2DbRefererHeader);
            return client;
        }

        public void Initialize(string pluginDirectory)
        {
            this.pluginDirectory = pluginDirectory;
            this.iconsDirectory = BuffIconCatalog.IconsDirectory(pluginDirectory);
            Directory.CreateDirectory(this.iconsDirectory);
            this.iconMap = BuffIconCatalog.LoadIconMap(pluginDirectory);
            this.ReloadTexturesInternal();
        }

        public void ReloadIconMap(string pluginDirectory)
        {
            this.iconMap = BuffIconCatalog.LoadIconMap(pluginDirectory);
        }

        public string LastLogLine => this.lastLogLine;

        public int CachedIconCount => Directory.Exists(this.iconsDirectory)
            ? Directory.EnumerateFiles(this.iconsDirectory).Count(path =>
                path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            : 0;

        public void RequestDownloads(IEnumerable<string> watchIds, bool force = false)
        {
            foreach (var id in watchIds.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (BuffIconCatalog.IsReservedResourceWatchId(id))
                {
                    continue;
                }

                this.QueueDownload(id, force);
            }
        }

        public void RequestResourceIcons(bool force = false)
        {
            foreach (var id in BuffIconCatalog.ResourceIconIds)
            {
                this.QueueDownload(id, force);
            }
        }

        public bool TryGetTexture(string watchId, out IntPtr ptr, out int w, out int h)
        {
            foreach (var extension in new[] { ".webp", ".png" })
            {
                var cacheName = BuffIconCatalog.BuildCacheFileName(watchId, extension);
                if (this.textures.TryGetValue(cacheName, out var tex) && tex.Ptr != IntPtr.Zero)
                {
                    ptr = tex.Ptr;
                    w = tex.W;
                    h = tex.H;
                    return true;
                }
            }

            ptr = IntPtr.Zero;
            w = 0;
            h = 0;
            return false;
        }

        public void PollReload()
        {
            if (Interlocked.Exchange(ref this.reloadRequested, 0) == 1)
            {
                this.ReloadTexturesInternal();
            }
        }

        public void ReloadTextures()
        {
            this.ReloadTexturesInternal();
        }

        private void QueueDownload(string watchId, bool force)
        {
            var cacheFile = BuffIconCatalog.BuildCacheFileName(watchId, ".webp");
            var localPath = Path.Combine(this.iconsDirectory, cacheFile);
            var legacyPng = Path.Combine(this.iconsDirectory, BuffIconCatalog.BuildCacheFileName(watchId, ".png"));

            if (force)
            {
                this.failedDownloads.Remove(cacheFile);
                TryDelete(localPath);
                TryDelete(legacyPng);
            }
            else if (File.Exists(localPath) || File.Exists(legacyPng))
            {
                return;
            }

            if (!force && this.failedDownloads.Contains(cacheFile))
            {
                return;
            }

            lock (this.sync)
            {
                if (!this.pendingDownloads.Add(cacheFile))
                {
                    return;
                }
            }

            _ = Task.Run(() => this.DownloadIconAsync(watchId, cacheFile, localPath));
        }

        private async Task DownloadIconAsync(string watchId, string cacheFile, string localPath)
        {
            try
            {
                string? relativeIconPath = null;
                if (BuffIconCatalog.TryResolveDirectIcon(watchId, this.iconMap.DirectIcons, out var direct))
                {
                    relativeIconPath = direct;
                }
                else
                {
                    if (!BuffIconCatalog.TryResolvePageSlug(watchId, this.iconMap.PageSlugs, out var pageSlug))
                    {
                        this.Log($"SKIP {watchId}: no poe2db page mapping");
                        this.failedDownloads.Add(cacheFile);
                        return;
                    }

                    var pageUrl = BuffIconCatalog.BuildPageUrl(pageSlug);
                    using var pageResponse = await Http.GetAsync(pageUrl).ConfigureAwait(false);
                    if (!pageResponse.IsSuccessStatusCode)
                    {
                        this.Log($"FAIL {watchId}: page HTTP {(int)pageResponse.StatusCode} ({pageSlug})");
                        this.failedDownloads.Add(cacheFile);
                        return;
                    }

                    var html = await pageResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!BuffIconCatalog.TryParseBuffIconPath(html, out var parsed))
                    {
                        this.Log($"FAIL {watchId}: no icon on poe2db ({pageSlug})");
                        this.failedDownloads.Add(cacheFile);
                        return;
                    }

                    relativeIconPath = parsed;
                }

                var imageUrl = BuffIconCatalog.BuildCdnUrl(relativeIconPath);
                using var imageResponse = await Http.GetAsync(imageUrl).ConfigureAwait(false);
                if (!imageResponse.IsSuccessStatusCode)
                {
                    this.Log($"FAIL {watchId}: CDN HTTP {(int)imageResponse.StatusCode} ({relativeIconPath})");
                    this.failedDownloads.Add(cacheFile);
                    return;
                }

                var bytes = await imageResponse.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                if (!BuffIconCatalog.IsSupportedImage(bytes))
                {
                    this.Log($"FAIL {watchId}: invalid image bytes ({relativeIconPath})");
                    this.failedDownloads.Add(cacheFile);
                    return;
                }

                await File.WriteAllBytesAsync(localPath, bytes).ConfigureAwait(false);
                this.Log($"OK {watchId}: {relativeIconPath}");
                this.failedDownloads.Remove(cacheFile);
                Interlocked.Exchange(ref this.reloadRequested, 1);
            }
            catch (Exception ex)
            {
                this.Log($"FAIL {watchId}: {ex.Message}");
                this.failedDownloads.Add(cacheFile);
            }
            finally
            {
                lock (this.sync)
                {
                    this.pendingDownloads.Remove(cacheFile);
                }
            }
        }

        private void ReloadTexturesInternal()
        {
            this.textures.Clear();
            if (!Directory.Exists(this.iconsDirectory))
            {
                return;
            }

            foreach (var pathname in Directory.EnumerateFiles(this.iconsDirectory))
            {
                if (!pathname.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) &&
                    !pathname.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var fileName = Path.GetFileName(pathname);
                Core.Overlay.AddOrGetImagePointer(pathname, false, out var handle, out var w, out var h);
                if (handle != IntPtr.Zero)
                {
                    this.textures[fileName] = (handle, (int)w, (int)h);
                }
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        private void Log(string line)
        {
            this.lastLogLine = line;
            try
            {
                var path = BuffIconCatalog.DownloadLogPath(this.pluginDirectory);
                File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {line}{Environment.NewLine}");
            }
            catch
            {
            }
        }
    }
}
