namespace Launcher
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.IO.Compression;
    using System.Net.Http;
    using System.Threading.Tasks;
    using Newtonsoft.Json.Linq;

    public static class AutoUpdate
    {
        private const string ReleasesApiUrl = "https://gitlab.com/api/v4/projects/arsenic2k%2FGameHelper2/releases";
        
        private static readonly HttpClient HttpClient = new();
        private static string extractedPath;
        private static string newVersion;

        static AutoUpdate()
        {
            HttpClient.DefaultRequestHeaders.Add("User-Agent", "GameHelper-Launcher");
        }

        public static async Task<bool> CheckAndUpdateAsync(string gameHelperExePath)
        {
            try
            {
                Console.WriteLine("Checking for updates...");
 
                var currentVersion = GetCurrentVersion(gameHelperExePath);
                var latestVersion = await GetLatestVersionAsync();
                if (string.IsNullOrEmpty(latestVersion))
                {
                    Console.WriteLine("Failed to check for updates.");
                    return false;
                }
 
                if (IsNewerVersion(latestVersion, currentVersion))
                {
                    Console.WriteLine($"New version available: {latestVersion}");
                    return await DownloadAndInstallUpdateAsync(latestVersion);
                }
 
                Console.WriteLine("No updates were found.");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Update check failed: {ex.Message}");
                return false;
            }
        }

        private static string GetCurrentVersion(string gameHelperExePath)
        {
            try
            {
                var versionInfo = FileVersionInfo.GetVersionInfo(gameHelperExePath);
                var version = versionInfo.FileVersion;
                if (string.IsNullOrEmpty(version) || version == "1.0.0.0")
                {
                    return "Dev";
                }
                var parts = version.Split('.');
                return $"v{parts[0]}.{parts[1]}.{parts[2]}";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to read version from {gameHelperExePath}: {ex.Message}");
                return "Dev";
            }
        }

        private static async Task<string> GetLatestVersionAsync()
        {
            try
            {
                var response = await HttpClient.GetStringAsync(ReleasesApiUrl);
                var releases = JArray.Parse(response);
                if (releases.Count > 0)
                {
                    return releases[0]["tag_name"]?.ToString() ?? null;
                }
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to get latest version: {ex.Message}");
                return null;
            }
        }

        private static bool IsNewerVersion(string latestVersion, string currentVersion)
        {
            try
            {
                if (!latestVersion.StartsWith('v'))
                    return false;
                
                var latest = latestVersion.TrimStart('v');
                var current = currentVersion.TrimStart('v');

                var latestParts = latest.Split('.');
                var currentParts = current.Split('.');

                for (int i = 0; i < Math.Max(latestParts.Length, currentParts.Length); i++)
                {
                    int latestPart = i < latestParts.Length ? int.Parse(latestParts[i]) : 0;
                    int currentPart = i < currentParts.Length ? int.Parse(currentParts[i]) : 0;

                    if (latestPart > currentPart) return true;
                    if (latestPart < currentPart) return false;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private static async Task<bool> DownloadAndInstallUpdateAsync(string version)
        {
            try
            {
                var downloadUrl = await GetDownloadUrlForVersionAsync(version);
                if (string.IsNullOrEmpty(downloadUrl))
                {
                    Console.WriteLine("Failed to get download URL for the update.");
                    return false;
                }

                var tempDir = Path.Combine(Path.GetTempPath(), "GameHelperUpdate");
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
                Directory.CreateDirectory(tempDir);

                var zipPath = Path.Combine(tempDir, "update.zip");

                Console.WriteLine("Downloading update...");
                await DownloadFileWithProgressAsync(downloadUrl, zipPath);

                Console.WriteLine("Extracting update...");
                var extractDir = Path.Combine(tempDir, "extracted");
                ZipFile.ExtractToDirectory(zipPath, extractDir);

                extractedPath = extractDir;
                newVersion = version;
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Update preparation failed: {ex.Message}");
                return false;
            }
        }

        private static async Task<string> GetDownloadUrlForVersionAsync(string version)
        {
            try
            {
                var response = await HttpClient.GetStringAsync(ReleasesApiUrl);
                var releases = JArray.Parse(response);

                foreach (var release in releases)
                {
                    var tagName = release["tag_name"]?.ToString();
                    if (tagName == version)
                    {
                        var links = release["assets"]["links"] as JArray;
                        if (links?.Count > 0)
                        {
                            return links[0]["url"]?.ToString();
                        }
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to get download URL: {ex.Message}");
                return null;
            }
        }

        private static async Task DownloadFileWithProgressAsync(string url, string destinationPath)
        {
            using (var response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                await using (var contentStream = await response.Content.ReadAsStreamAsync())
                {
                    await using (var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        var buffer = new byte[8192];
                        long totalDownloaded = 0;
                        int bytesRead;

                        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            totalDownloaded += bytesRead;

                            if (totalBytes > 0)
                            {
                                var percentage = (int)((totalDownloaded * 100) / totalBytes);
                                DrawProgressBar(percentage, 50);
                            }
                        }
                    }
                }
            }
            Console.WriteLine();
        }

        private static void DrawProgressBar(int percentage, int barLength)
        {
            var filled = (int)((percentage / 100.0) * barLength);
            var bar = new string('█', filled) + new string('░', barLength - filled);

            Console.Write($"\r[{bar}] {percentage}%");
        }

        public static void LaunchUpdateAndExit()
        {
            try
            {
                if (string.IsNullOrEmpty(extractedPath) || string.IsNullOrEmpty(newVersion))
                {
                    Console.WriteLine("Update paths not initialized.");
                    return;
                }

                var currentDir = AppDomain.CurrentDomain.BaseDirectory;
                var parentDir = Directory.GetParent(currentDir)?.FullName;
                var launcherPath = Path.Combine(currentDir, "Launcher.exe");
                var tempDir = Path.Combine(Path.GetTempPath(), "GameHelperUpdate");

                if (string.IsNullOrEmpty(parentDir))
                {
                    Console.WriteLine("Could not determine installation directory.");
                    return;
                }

                var psCommand = $@"
Write-Host 'Waiting for GameHelper Launcher to exit...';
$launcherProcess = Get-Process -Name 'Launcher' -ErrorAction SilentlyContinue;
if ($launcherProcess) {{
    Wait-Process -Name 'Launcher' -ErrorAction SilentlyContinue;
    Write-Host 'Launcher has exited.';
}} else {{
    Write-Host 'Launcher process not found, proceeding...';
}}

Write-Host 'Installing update...';
try {{
    Copy-Item -Path '{extractedPath}\*' -Destination '{parentDir}' -Recurse -Force;
    Write-Host 'Update completed successfully!';
    Write-Host 'Restarting GameHelper Launcher...';
    Start-Process -FilePath '{launcherPath}' -WorkingDirectory '{currentDir}';
    $timeout = 0;
    do {{
        Start-Sleep -Milliseconds 100;
        $newLauncherProcess = Get-Process -Name 'Launcher' -ErrorAction SilentlyContinue;
        $timeout++;
    }} while (-not $newLauncherProcess -and $timeout -lt 10);
    if ($newLauncherProcess) {{
        Write-Host 'Launcher restarted successfully.';
    }} else {{
        Write-Host 'Warning: Could not verify launcher restart.';
    }}
    Remove-Item -Path '{tempDir}' -Recurse -Force -ErrorAction SilentlyContinue;
    Write-Host 'Update process completed.';
}} catch {{
    Write-Host 'Update failed:' $_.Exception.Message;
    Read-Host 'Press Enter to continue';
}}";

                Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{psCommand}\"",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Normal,
                    CreateNoWindow = false
                });

                Console.WriteLine("Updating GameHelper. Launcher will restart after update is completed.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to launch update process: {ex.Message}");
            }
        }
    }
}
