using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// xUnit class fixture that boots AutoNate.Web as a child process on a random
/// port — auto-login disabled, Dapr probe skipped, SpaProxy dormant — and owns
/// a Playwright browser. Tests get an isolated <see cref="IBrowserContext"/>
/// preconfigured with the bound BaseURL via <see cref="NewContextAsync"/>.
///
/// First run rebuilds the SPA into wwwroot/, so it can take 30-60s.
/// </summary>
public sealed class AutoNateE2EFixture : IAsyncLifetime
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(3);

    private Process? _appProcess;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public string BaseUrl { get; private set; } = string.Empty;

    public IBrowser Browser =>
        _browser ?? throw new InvalidOperationException("Fixture not initialized.");

    public async Task InitializeAsync()
    {
        var repoRoot = FindRepoRoot();
        WipeStaleStaticWebAssetsManifests(repoRoot);
        BaseUrl = await StartAppAsync(repoRoot);

        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            // Headless by default; set PWDEBUG=1 to watch the browser.
            Headless = Environment.GetEnvironmentVariable("PWDEBUG") != "1"
        });
    }

    public Task<IBrowserContext> NewContextAsync() =>
        Browser.NewContextAsync(new BrowserNewContextOptions { BaseURL = BaseUrl });

    public async Task DisposeAsync()
    {
        if (_browser is not null)
        {
            await _browser.DisposeAsync();
        }
        _playwright?.Dispose();

        if (_appProcess is { HasExited: false })
        {
            try
            {
                _appProcess.Kill(entireProcessTree: true);
                _appProcess.WaitForExit(5_000);
            }
            catch
            {
                // Best effort. The host may already have shut itself down.
            }
        }
        _appProcess?.Dispose();
    }

    private async Task<string> StartAppAsync(string repoRoot)
    {
        // BuildSpa=true forces the .csproj target that runs `npm run build` and
        // mirrors dist/ into wwwroot/, so the host can serve the SPA directly
        // without SpaProxy.
        var info = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        info.ArgumentList.Add("run");
        info.ArgumentList.Add("--project");
        info.ArgumentList.Add("src/AutoNate.Web");
        info.ArgumentList.Add("--no-launch-profile");
        info.ArgumentList.Add("-p:BuildSpa=true");

        // Random Kestrel port — we'll pick the actual URL out of the host's
        // "Now listening on: ..." log line.
        info.Environment["ASPNETCORE_URLS"] = "http://127.0.0.1:0";
        // Skip the dev Dapr sidecar probe so the host doesn't refuse to start.
        info.Environment["AUTONATE_ALLOW_RUNNING_WITHOUT_DAPR"] = "true";
        // The whole point: the user-typed login flow is unreachable when
        // auto-login signs every GET in. Always off for E2E.
        info.Environment["DevelopmentAutoLogin__Enabled"] = "false";
        // Stay in Development so HTTPS redirect/HSTS don't kick in. SpaProxy
        // only loads when ASPNETCORE_HOSTINGSTARTUPASSEMBLIES references it,
        // which --no-launch-profile keeps unset.
        info.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";

        _appProcess = new Process { StartInfo = info };

        var listeningUrlSource = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var stdoutBuffer = new List<string>();
        var stderrBuffer = new List<string>();

        _appProcess.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            stdoutBuffer.Add(e.Data);

            // Kestrel logs e.g. "Now listening on: http://127.0.0.1:54321"
            var match = Regex.Match(e.Data, @"Now listening on:\s*(http://\S+)");
            if (match.Success)
            {
                listeningUrlSource.TrySetResult(match.Groups[1].Value.TrimEnd('/'));
            }
        };
        _appProcess.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) stderrBuffer.Add(e.Data);
        };
        _appProcess.Exited += (_, _) =>
        {
            listeningUrlSource.TrySetException(new InvalidOperationException(
                $"AutoNate.Web exited with code {_appProcess?.ExitCode} before reaching the listening state.\n" +
                $"--- stdout ---\n{string.Join('\n', stdoutBuffer)}\n" +
                $"--- stderr ---\n{string.Join('\n', stderrBuffer)}"));
        };
        _appProcess.EnableRaisingEvents = true;

        _appProcess.Start();
        _appProcess.BeginOutputReadLine();
        _appProcess.BeginErrorReadLine();

        try
        {
            return await listeningUrlSource.Task.WaitAsync(StartupTimeout);
        }
        catch (TimeoutException)
        {
            throw new TimeoutException(
                $"AutoNate.Web did not start within {StartupTimeout.TotalSeconds:F0}s.\n" +
                $"--- stdout ---\n{string.Join('\n', stdoutBuffer)}\n" +
                $"--- stderr ---\n{string.Join('\n', stderrBuffer)}");
        }
    }

    /// <summary>
    /// Static-web-assets manifests on disk reference hashed Vite filenames
    /// (e.g. fa-brands-400-ABCDEFGH.woff2). When BuildSpa=true wipes wwwroot/
    /// and Vite emits new hashes, leftover manifests from a prior build still
    /// point at the old names — causing DefineStaticWebAssets to throw
    /// "No file exists for the asset". The csproj target tries to clean these
    /// for the current Configuration only, so manifests from other Configurations
    /// (or copied into peer test projects' bin/) survive. Wipe them all here
    /// before building.
    /// </summary>
    private static void WipeStaleStaticWebAssetsManifests(string repoRoot)
    {
        var roots = new List<string> { Path.Combine(repoRoot, "src", "AutoNate.Web") };
        var testsDir = Path.Combine(repoRoot, "tests");
        if (Directory.Exists(testsDir))
        {
            roots.AddRange(Directory.EnumerateDirectories(testsDir));
        }

        foreach (var root in roots)
        {
            foreach (var sub in new[] { "obj", "bin" })
            {
                var path = Path.Combine(root, sub);
                if (!Directory.Exists(path)) continue;

                foreach (var pattern in new[]
                         {
                             "*.staticwebassets.*.json",
                             "staticwebassets.*.json",
                             "*.dswa.cache.json",
                         })
                {
                    foreach (var file in Directory.EnumerateFiles(path, pattern, SearchOption.AllDirectories))
                    {
                        try { File.Delete(file); } catch { /* best effort */ }
                    }
                }
            }
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AutoNate.sln")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            $"Could not find AutoNate.sln walking up from {AppContext.BaseDirectory}.");
    }
}
