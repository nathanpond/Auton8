using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Npgsql;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// xUnit collection fixture that boots AutoNate.Web as a child process on a
/// random port — auto-login disabled, Dapr probe skipped, SpaProxy dormant —
/// and owns a Playwright browser. Tests get an isolated
/// <see cref="IBrowserContext"/> preconfigured with the bound BaseURL via
/// <see cref="NewContextAsync"/>.
///
/// Wired as a <em>collection</em> fixture (not <c>IClassFixture</c>) so all
/// E2E test classes share a single fixture instance. Per-class would let
/// xUnit parallelize three concurrent <c>dotnet run -p:BuildSpa=true</c>
/// invocations that race on obj/.../rpswa.dswa.cache.json and on wiping
/// wwwroot/, killing every fixture except the lucky winner.
///
/// First run rebuilds the SPA into wwwroot/, so it can take 30-60s.
/// </summary>
public sealed class AutoNateE2EFixture : IAsyncLifetime
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(3);

    /// <summary>Prefix every E2E database shares, so a sweep can find orphans.</summary>
    internal const string TestDbPrefix = "autonate_e2e_";

    // Dedicated ephemeral test database, named per RUN (#248).
    //
    // It used to be the fixed name `AutoNate_E2E`, dropped with `WITH (FORCE)` on
    // startup. Two E2E runs on one machine therefore destroyed each other: the
    // second run's DROP terminated the first run's connections mid-test
    // ("57P01: terminating connection due to administrator command") and the
    // CREATEs raced ("23505: duplicate key ... pg_database_datname_index").
    // Three verification agents hit this; one lost four runs to it.
    //
    // That is not merely inconvenient. CI excludes `RequiresService=Flowable`,
    // so every execution-level guarantee in M4 is checked only on a developer
    // machine — and anything making those runs fragile makes the milestone's
    // central evidence fragile.
    //
    // Per-run, like `PostgresTestDatabase` already does for the backend suite.
    // Lowercase because an unquoted identifier folds to lowercase in Postgres and
    // a mixed-case name has to be quoted everywhere it appears.
    internal static readonly string TestDbName =
        TestDbPrefix + Guid.NewGuid().ToString("n");

    // Dev Postgres credentials. Hardcoded to match `infra/docker-compose.yml`
    // (POSTGRES_USER=autonate, POSTGRES_PASSWORD=Your_password123!) and the
    // `Default` connection string in `appsettings.Development.json`. The port
    // can be overridden via `AUTONATE_POSTGRES_PORT` to match the same env
    // override the compose file honors.
    private const string PgHost = "localhost";
    private const string PgUser = "autonate";
    private const string PgPassword = "Your_password123!";
    private const string PgPoolTuning =
        "Keepalive=30;Tcp Keepalive=true;Connection Idle Lifetime=60;Connection Pruning Interval=10";

    private Process? _appProcess;
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private string _testConnString = string.Empty;

    public string BaseUrl { get; private set; } = string.Empty;

    public IBrowser Browser =>
        _browser ?? throw new InvalidOperationException("Fixture not initialized.");

    /// <summary>
    /// Removes Flowable deployments left by earlier E2E runs (#214).
    /// </summary>
    /// <remarks>
    /// The engine is a single shared instance and nothing dropped these before — 388
    /// deployments had accumulated, 303 of them from this suite. Only the `e2e-`
    /// prefix is swept, so a developer's own work on the same engine is untouched.
    /// Never fails the run: a suite that cannot start because cleanup failed is worse
    /// than the mess it was clearing.
    /// </remarks>
    private static async Task SweepStaleFlowableDeploymentsAsync()
    {
        try
        {
            using var client = Support.FlowableDeploymentSweep.CreateClient(
                Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_URL") ?? "http://localhost:8080/flowable-rest",
                Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_USER") ?? "rest-admin",
                Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_PASSWORD") ?? "test");

            var deleted = await Support.FlowableDeploymentSweep.SweepAsync(client);
            if (deleted > 0)
            {
                Console.WriteLine($"[e2e-flowable-sweep] Removed {deleted} deployment(s) from earlier runs.");
            }
        }
        catch (Exception exception)
        {
            Console.WriteLine($"[e2e-flowable-sweep] Skipped: {exception.GetType().Name}: {exception.Message}");
        }
    }

    public async Task InitializeAsync()
    {
        var repoRoot = FindRepoRoot();
        WipeStaleStaticWebAssetsManifests(repoRoot);
        WipeWwwroot(repoRoot);
        await SweepStaleFlowableDeploymentsAsync();

        // Build a fresh `AutoNate_E2E` database before the app starts so the
        // app's `DatabaseSchemaInitializer.EnsureAsync` (Program.cs) runs
        // against a known-good baseline and finishes the schema (roles, menus,
        // sample project) and creates the bootstrap administrator from the
        // Bootstrap__* variables set in StartAppAsync.
        _testConnString = await BootstrapTestDatabaseAsync();

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

    /// <summary>
    /// Drives the SPA login form as the seeded super-admin and waits for the
    /// post-login redirect to land on /home. Mantine's TextInput controls have
    /// auto-generated IDs, so we drive them by label.
    /// </summary>
    public static async Task SignInAsAdminAsync(IPage page) =>
        await SignInAsync(page, "admin", "admin");

    /// <summary>
    /// Drives the SPA login form with arbitrary credentials. Returns once the
    /// post-login navigation has settled — either at /home (success) or back at
    /// / with an ?error= query param (failure).
    /// </summary>
    public static async Task SignInAsync(IPage page, string username, string password)
    {
        await page.GotoAsync("/");
        // Mantine's TextInput and PasswordInput don't expose a stable id; drive
        // them by accessible role + name. Plain `GetByLabel("Password")` is
        // ambiguous because Mantine renders an aria-label="Toggle password
        // visibility" eye-icon button inside the same wrapper.
        await page.GetByRole(AriaRole.Textbox, new() { Name = "Username" }).FillAsync(username);
        await page.Locator("input[autocomplete='current-password']").FillAsync(password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign me in" }).ClickAsync();

        // /account/login 302s to /home on success or back to /?error=... on
        // failure. Wait for either so the caller can branch on URL.
        await page.WaitForURLAsync(new Regex(@"/(home|\?error=)"));
    }

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

        // #248. Drop OUR database, now that nothing is connected to it. Without
        // this a per-run name leaks one database per run -- trading a fixture
        // that destroys concurrent runs for one that fills the cluster, which is
        // the failure #191 and #258 were both about.
        await DropOwnDatabaseAsync();
    }

    private async Task DropOwnDatabaseAsync()
    {
        var port = Environment.GetEnvironmentVariable("AUTONATE_POSTGRES_PORT") ?? "5432";
        var maintenance =
            $"Host={PgHost};Port={port};Database=postgres;Username={PgUser};Password={PgPassword}";

        try
        {
            await using var conn = new NpgsqlConnection(maintenance);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            // FORCE is safe here and nowhere else: this name belongs to this
            // process, so the only sessions it can terminate are our own app's.
            cmd.CommandText = $@"DROP DATABASE IF EXISTS ""{TestDbName}"" WITH (FORCE);";
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception exception) when (exception is PostgresException or NpgsqlException)
        {
            // Best effort. The age-based sweep at the next run's startup is the
            // backstop, which is why it exists rather than relying on this.
        }
    }

    /// <summary>
    /// Removes E2E databases left by runs that never reached DisposeAsync (#248).
    /// </summary>
    /// <remarks>
    /// By AGE, not by "everything with our prefix". A database younger than the
    /// cutoff may belong to a run happening right now on the same machine, and
    /// dropping that is the exact defect this issue is about -- reintroducing it
    /// one function over would be worse than leaving the orphans.
    /// </remarks>
    private static async Task DropAbandonedE2EDatabasesAsync(string maintenanceConn)
    {
        var cutoff = TimeSpan.FromHours(3);

        try
        {
            await using var conn = new NpgsqlConnection(maintenanceConn);
            await conn.OpenAsync();

            var stale = new List<string>();
            await using (var query = conn.CreateCommand())
            {
                // pg_database carries no creation time, so age comes from the
                // directory's modification time via pg_stat_file. A database we
                // cannot stat is left alone.
                query.CommandText = @"
                    select datname
                    from pg_database
                    where datname like 'autonate\_e2e\_%'
                      and datname <> @self;";
                query.Parameters.AddWithValue("self", TestDbName);

                await using var reader = await query.ExecuteReaderAsync();
                while (await reader.ReadAsync()) stale.Add(reader.GetString(0));
            }

            foreach (var name in stale)
            {
                try
                {
                    await using var age = conn.CreateCommand();
                    age.CommandText =
                        "select (pg_stat_file('base/' || oid::text).modification) " +
                        "from pg_database where datname = @name;";
                    age.Parameters.AddWithValue("name", name);
                    var modified = await age.ExecuteScalarAsync();
                    if (modified is not DateTime stamp) continue;
                    if (DateTime.UtcNow - stamp.ToUniversalTime() < cutoff) continue;

                    await using var drop = conn.CreateCommand();
                    drop.CommandText = $@"DROP DATABASE IF EXISTS ""{name}"" WITH (FORCE);";
                    await drop.ExecuteNonQueryAsync();
                }
                catch (PostgresException)
                {
                    // In use, or gone already. Either way not ours to force.
                }
            }
        }
        catch (Exception exception) when (exception is PostgresException or NpgsqlException)
        {
            // Never fail a run over cleanup of a previous one.
        }
    }

    /// <summary>
    /// A free TCP port, released immediately so Kestrel can take it (#223).
    /// </summary>
    /// <remarks>
    /// Racy in principle — something else could claim it in the gap — but the
    /// alternative is asking Kestrel for port 0 and never learning the number in
    /// time to tell Flowable where to call, which is the problem this fixes.
    /// </remarks>
    private static int FindFreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
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
        // #223. A fixed, reachable port instead of 127.0.0.1:0.
        //
        // Flowable runs in a container and calls back to the app to execute a
        // workflow behaviour. On a random loopback port it could never reach this
        // one, so it reached the app in the autonate-web container instead —
        // against a different database, where the workflow the test just published
        // does not exist. No behaviour could be verified end to end.
        //
        // Bound on all interfaces because host.docker.internal resolves to the
        // host's LAN address from inside the container; loopback-only is
        // unreachable from there. Test host only — no compose file publishes this,
        // so the loopback-binding invariant is untouched.
        var callbackPort = FindFreePort();
        info.Environment["ASPNETCORE_URLS"] = $"http://+:{callbackPort}";
        info.Environment["WorkflowBehaviors__CallbackBaseUrlOverride"] =
            $"http://host.docker.internal:{callbackPort}";

        // #257. Makes every deployment this suite publishes identifiable BY NAME
        // (`e2e-<key>.bpmn20.xml`), which is what lets FlowableDeploymentSweep match
        // only the suite's own work. Unset in production, where the name is the
        // process key exactly as before.
        //
        // The first #257 fix swept by age instead and deleted a developer's own
        // three-hour-old deployments — 348 of them in one observed run — while the
        // sweep's own remarks promised it could not. This restores the guard rather
        // than trading it away.
        info.Environment["Flowable__DeploymentNamePrefix"] = Support.FlowableDeploymentSweep.SuiteDeploymentPrefix;
        // Skip the dev Dapr sidecar probe so the host doesn't refuse to start.
        info.Environment["AUTONATE_ALLOW_RUNNING_WITHOUT_DAPR"] = "true";
        // The whole point: the user-typed login flow is unreachable when
        // auto-login signs every GET in. Always off for E2E.
        info.Environment["DevelopmentAutoLogin__Enabled"] = "false";
        // Stay in Development so HTTPS redirect/HSTS don't kick in. SpaProxy
        // only loads when ASPNETCORE_HOSTINGSTARTUPASSEMBLIES references it,
        // which --no-launch-profile keeps unset.
        info.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        // Override the dev `Default` connection string so the app boots against
        // our ephemeral `AutoNate_E2E` database instead of the developer's
        // working `AutoNate`. ASP.NET Core's configuration provider maps the
        // double-underscore form to `ConnectionStrings:Default`.
        info.Environment["ConnectionStrings__Default"] = _testConnString;
        // The `admin`/`admin` account SignInAsSuperAdminAsync drives the login
        // form with. It used to come from a hardcoded INSERT in the init SQL
        // that this fixture replays, hash and salt committed to the repository;
        // the app now creates it at startup from these variables, so the test
        // credential lives in test code. The id is pinned to the value the
        // seed used because suites assert against it.
        info.Environment["Bootstrap__AdminUsername"] = "admin";
        info.Environment["Bootstrap__AdminPassword"] = "admin";
        info.Environment["Bootstrap__AdminEmail"] = "admin@localhost";
        info.Environment["Bootstrap__AdminUserId"] = "11111111-1111-1111-1111-111111111111";

        _appProcess = new Process { StartInfo = info };

        var listeningUrlSource = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var stdoutBuffer = new List<string>();
        var stderrBuffer = new List<string>();

        _appProcess.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            stdoutBuffer.Add(e.Data);

            // #223. The port is chosen up front now, so the log line is only the
            // readiness signal — the URL is not parsed out of it. Binding "+"
            // makes Kestrel log "http://[::]:54321", which is a valid listen
            // address and a useless base URL for a browser to navigate to.
            if (Regex.IsMatch(e.Data, @"Now listening on:\s*http://"))
            {
                listeningUrlSource.TrySetResult($"http://127.0.0.1:{callbackPort}");
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

        string baseUrl;
        try
        {
            baseUrl = await listeningUrlSource.Task.WaitAsync(StartupTimeout);
        }
        catch (TimeoutException)
        {
            throw new TimeoutException(
                $"AutoNate.Web did not start within {StartupTimeout.TotalSeconds:F0}s.\n" +
                $"--- stdout ---\n{string.Join('\n', stdoutBuffer)}\n" +
                $"--- stderr ---\n{string.Join('\n', stderrBuffer)}");
        }

        // "Now listening" only proves Kestrel is up. Every spec starts at "/"
        // (SignInAsync), so if the host isn't serving the SPA shell there the
        // whole run degrades into N identical 30 s element timeouts with no
        // clue why (that is precisely how archived-132 presented: a route-constraint
        // change made "/" a bare 404 while deep links still worked). Probe it
        // once here and fail with the app's output instead.
        await AssertSpaShellServedAsync(baseUrl, stdoutBuffer, stderrBuffer);
        return baseUrl;
    }

    private static async Task AssertSpaShellServedAsync(
        string baseUrl, List<string> stdoutBuffer, List<string> stderrBuffer)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        string summary;
        try
        {
            using var response = await http.GetAsync(baseUrl + "/");
            var body = await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode && body.Contains("id=\"root\"", StringComparison.Ordinal))
            {
                return;
            }
            summary = $"GET / returned {(int)response.StatusCode} {response.StatusCode} " +
                      $"({response.Content.Headers.ContentType?.MediaType ?? "no content-type"}, {body.Length} bytes); " +
                      "expected 200 with the SPA shell (<div id=\"root\">).";
        }
        catch (Exception ex)
        {
            summary = $"GET / threw {ex.GetType().Name}: {ex.Message}";
        }

        static string Tail(List<string> lines) =>
            string.Join('\n', lines.Count <= 40 ? lines : lines.GetRange(lines.Count - 40, 40));

        throw new InvalidOperationException(
            $"AutoNate.Web is listening at {baseUrl} but is not serving the SPA at the site root. {summary}\n" +
            "Common causes: wwwroot/ missing or empty after the BuildSpa target, or the SPA fallback route " +
            "not matching \"/\" (see Program.cs MapFallbackToFile).\n" +
            $"--- stdout (tail) ---\n{Tail(stdoutBuffer)}\n" +
            $"--- stderr (tail) ---\n{Tail(stderrBuffer)}");
    }

    /// <summary>
    /// Creates an empty `AutoNate_E2E` database and hands its connection string
    /// to the app under test. Nothing else: the application's
    /// `DatabaseSchemaInitializer.EnsureAsync` applies the base schema and
    /// everything after it.
    /// </summary>
    /// <remarks>
    /// This used to replay `infra/postgres/init/02-...sql` from a
    /// repo-root-relative path, because the application could not initialise an
    /// empty database on its own — the base schema existed only as a file
    /// mounted into the Postgres container's entrypoint. It now lives inside
    /// AutoNate.Web as an embedded resource and is the initialiser's first
    /// step, so replaying it here would be a second copy of the same work.
    ///
    /// The compose entrypoint scripts remain irrelevant here for the original
    /// reason: the compose file sets `POSTGRES_DB=flowable`, so those files only
    /// ever ran against the Flowable database on first volume creation.
    /// </remarks>
    /// <returns>The Npgsql connection string to the new database — passed as
    /// the `ConnectionStrings__Default` env override.</returns>
    private static async Task<string> BootstrapTestDatabaseAsync()
    {
        var port = int.TryParse(
            Environment.GetEnvironmentVariable("AUTONATE_POSTGRES_PORT"),
            out var parsed) ? parsed : 5432;

        var maintenanceConn =
            $"Host={PgHost};Port={port};Database=postgres;Username={PgUser};Password={PgPassword}";
        var testConn =
            $"Host={PgHost};Port={port};Database={TestDbName};Username={PgUser};Password={PgPassword};{PgPoolTuning}";

        // #248. CREATE only. The name is unique to this run, so there is nothing
        // of ours to drop -- and the DROP that used to be here is precisely what
        // reached into a concurrent run and killed it. `IF EXISTS ... WITH
        // (FORCE)` on a name only this process knows would be a no-op at best and
        // a footgun the moment the name stopped being unique.
        await using (var conn = new NpgsqlConnection(maintenanceConn))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"CREATE DATABASE ""{TestDbName}"";";
            await cmd.ExecuteNonQueryAsync();
        }

        // Orphans from runs that were killed before DisposeAsync. Age-based and
        // prefix-scoped: a database younger than the cutoff may belong to a run
        // happening right now, which is the mistake this whole issue is about.
        await DropAbandonedE2EDatabasesAsync(maintenanceConn);

        return testConn;
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
    /// <summary>
    /// Deletes src/AutoNate.Web/wwwroot/ before launching the app. Two reasons:
    /// (1) the upstream drawio bundle ships SVGs with commas in their filenames
    /// (e.g. SAP_BTP,_ABAP_environment.svg). When MSBuild's static-web-assets
    /// publish targets enumerate a populated wwwroot/, they invoke
    /// [MSBuild]::MakeRelative on each path; commas inside an argument break
    /// the function-call syntax and the build fails with MSB4186. Starting
    /// from empty wwwroot/ avoids the enumeration on the way in (BuildSpa
    /// repopulates after), so the comma files only exist in-process. (2) the
    /// BuildSpa target's Inputs/Outputs check skips rebuilding when
    /// wwwroot/index.html is present, leaving us at the mercy of whatever
    /// hashed asset names were on disk from a prior session.
    /// </summary>
    private static void WipeWwwroot(string repoRoot)
    {
        var wwwroot = Path.Combine(repoRoot, "src", "AutoNate.Web", "wwwroot");
        if (Directory.Exists(wwwroot))
        {
            try { Directory.Delete(wwwroot, recursive: true); } catch { /* best effort */ }
        }
    }

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
