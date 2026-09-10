using System;
using System.IO;
using System.Threading.Tasks;
using GeoDataPro.Core;
using GeoDataPro.Core.Data;
using GeoDataPro.Core.Diagnostics;
using GeoDataPro.Core.Security;

namespace GeoDataPro.Tests;

public sealed class MutableClock : IClock
{
    public MutableClock(DateTimeOffset? start = null) =>
        UtcNow = start ?? new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    public DateTimeOffset UtcNow { get; private set; }

    public void Advance(TimeSpan by) => UtcNow += by;
}

public sealed class TestHost : IDisposable
{
    public const string AdminPassword = "Qorabuloq!2026#Geo";
    public const string OperatorPassword = "Zarafshon!2026#Op";
    public const string ViewerPassword = "Nurota!2026#View";

    readonly string _root;
    bool _disposed;

    TestHost(string root, SecurityHost host, MutableClock clock)
    {
        _root = root;
        Host = host;
        Clock = clock;
    }

    public SecurityHost Host { get; }
    public MutableClock Clock { get; }
    public string Root => _root;

    public static TestHost Create(MutableClock? clock = null)
    {
        var effectiveClock = clock ?? new MutableClock();
        var root = Path.Combine(Path.GetTempPath(), "gdp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        DatabaseLocation.Reset();

        var paths = new DefaultPlatformPaths(root);
        var storage = new InMemorySecureStorage();
        var hasher = new Argon2PasswordHasher(memoryKib: 8192, iterations: 2, parallelism: 1);
        var host = SecurityHost.Build(paths, storage, "test", DiagnosticLevel.Debug, effectiveClock, hasher);
        host.PrepareStorage();

        return new TestHost(root, host, effectiveClock);
    }

    public async Task<int> SeedAdminAsync()
    {
        await Host.Authentication.ProvisionFirstAdminAsync("admin", "Administrator", AdminPassword);
        var result = await Host.Authentication.LoginAsync("admin", AdminPassword);
        return result.Principal!.UserId;
    }

    public async Task SeedUserAsync(string username, string password, AppRole role)
    {
        await Host.Users.CreateAsync(username, username, password, role);

        await using var db = Host.Database.Create();
        var normalized = AuthenticationService.Normalize(username);
        var user = db.Users.Single(x => x.UsernameNormalized == normalized);
        user.MustChangePassword = false;
        await db.SaveChangesAsync();
    }

    public Task<AuthResult> LoginAsync(string username, string password) =>
        Host.Authentication.LoginAsync(username, password);

    public string DatabaseFile => DatabaseLocation.DatabaseFile;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Host.Dispose();
        DatabaseLocation.Reset();

        try { Directory.Delete(_root, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
