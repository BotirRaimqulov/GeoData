using System;
using System.Linq;
using System.Threading.Tasks;
using GeoDataPro.Core.Data;
using GeoDataPro.Core.Security;
using Xunit;

namespace GeoDataPro.Tests;

public class AuthenticationTests
{
    [Fact]
    public async Task WrongPasswordIsRejected()
    {
        using var host = TestHost.Create();
        await host.SeedAdminAsync();
        host.Host.Sessions.Clear();

        var result = await host.LoginAsync("admin", "wrong-password-123!");

        Assert.False(result.Succeeded);
        Assert.Equal(AuthOutcome.InvalidCredentials, result.Outcome);
        Assert.Null(host.Host.Sessions.Current);
    }

    [Fact]
    public async Task UnknownUserIsRejectedWithoutDisclosure()
    {
        using var host = TestHost.Create();
        await host.SeedAdminAsync();
        host.Host.Sessions.Clear();

        var unknown = await host.LoginAsync("no-such-user", TestHost.AdminPassword);
        var wrong = await host.LoginAsync("admin", "some-other-value-99!");

        Assert.Equal(unknown.Outcome, wrong.Outcome);
    }

    [Fact]
    public async Task BruteForceIsThrottledThenLockedOut()
    {
        var clock = new MutableClock();
        using var host = TestHost.Create(clock);
        await host.SeedAdminAsync();
        host.Host.Sessions.Clear();

        AuthResult last = new(AuthOutcome.InvalidCredentials);
        for (int i = 0; i < 5; i++)
            last = await host.LoginAsync("admin", "bad-attempt-" + i + "!");

        Assert.Equal(AuthOutcome.InvalidCredentials, last.Outcome);

        var blocked = await host.LoginAsync("admin", TestHost.AdminPassword);
        Assert.Equal(AuthOutcome.Throttled, blocked.Outcome);
        Assert.True(blocked.RetryAfter > TimeSpan.Zero);

        clock.Advance(TimeSpan.FromMinutes(1));

        await using var db = host.Host.Database.Create();
        var user = db.Users.Single();
        Assert.Equal(5, user.FailedAttempts);
    }

    [Fact]
    public async Task AccountLocksOutAfterRepeatedFailures()
    {
        var clock = new MutableClock();
        using var host = TestHost.Create(clock);
        await host.SeedAdminAsync();
        host.Host.Sessions.Clear();

        for (int i = 0; i < AuthenticationService.MaxFailedAttempts; i++)
        {
            if (i > 0) clock.Advance(LoginThrottle.Window + TimeSpan.FromMinutes(1));
            await host.LoginAsync("admin", "bad-value-" + i + "!");
        }

        var result = await host.LoginAsync("admin", TestHost.AdminPassword);
        Assert.Equal(AuthOutcome.LockedOut, result.Outcome);

        clock.Advance(AuthenticationService.LockoutDuration + TimeSpan.FromMinutes(1));
        var recovered = await host.LoginAsync("admin", TestHost.AdminPassword);
        Assert.True(recovered.Succeeded);
    }

    [Fact]
    public async Task PasswordIsNeverStoredInPlaintextOrReversibly()
    {
        using var host = TestHost.Create();
        await host.SeedAdminAsync();

        await using var db = host.Host.Database.Create();
        var user = db.Users.Single();

        Assert.DoesNotContain(TestHost.AdminPassword, user.PasswordHash, StringComparison.Ordinal);
        Assert.StartsWith("$argon2id$v=19$", user.PasswordHash, StringComparison.Ordinal);

        var bytes = System.IO.File.ReadAllBytes(host.DatabaseFile);
        var text = System.Text.Encoding.UTF8.GetString(bytes);
        Assert.DoesNotContain(TestHost.AdminPassword, text, StringComparison.Ordinal);
    }

    [Fact]
    public void IdenticalPasswordsProduceDifferentHashes()
    {
        var hasher = new Argon2PasswordHasher(memoryKib: 8192, iterations: 2, parallelism: 1);

        var a = hasher.Hash("Same!Password#2026");
        var b = hasher.Hash("Same!Password#2026");

        Assert.NotEqual(a, b);
        Assert.True(hasher.Verify("Same!Password#2026", a));
        Assert.True(hasher.Verify("Same!Password#2026", b));
        Assert.False(hasher.Verify("Same!Password#2027", a));
    }

    [Theory]
    [InlineData("short1!A")]
    [InlineData("alllowercaseletters")]
    [InlineData("password")]
    [InlineData("aaaaaaaaaaaaaaaa")]
    public void WeakPasswordsAreRejected(string candidate)
    {
        var policy = new PasswordPolicy();
        Assert.False(policy.Validate(candidate, "admin", "Administrator").Accepted);
    }

    [Fact]
    public void PasswordContainingUsernameIsRejected()
    {
        var policy = new PasswordPolicy();
        var check = policy.Validate("geologist!Aa2026", "geologist", "Geologist");
        Assert.False(check.Accepted);
        Assert.Equal(PasswordRejection.ContainsIdentity, check.Reason);
    }

    [Fact]
    public void StrongPasswordIsAccepted()
    {
        var policy = new PasswordPolicy();
        Assert.True(policy.Validate(TestHost.AdminPassword, "admin", "Administrator").Accepted);
    }

    [Fact]
    public void MalformedHashDoesNotThrow()
    {
        var hasher = new Argon2PasswordHasher(memoryKib: 8192, iterations: 2, parallelism: 1);

        Assert.False(hasher.Verify("anything", null));
        Assert.False(hasher.Verify("anything", ""));
        Assert.False(hasher.Verify("anything", "not-a-hash"));
        Assert.False(hasher.Verify("anything", "$argon2id$v=19$m=1,t=1,p=1$xx$yy"));
        Assert.False(hasher.Verify("anything", "$argon2id$v=19$m=65536,t=3,p=4$@@@$@@@"));
    }

    [Fact]
    public async Task LogoutInvalidatesSession()
    {
        using var host = TestHost.Create();
        await host.SeedAdminAsync();
        Assert.NotNull(host.Host.Sessions.Current);

        await host.Host.Authentication.LogoutAsync();

        Assert.Null(host.Host.Sessions.Current);
        Assert.Throws<NotAuthenticatedException>(() => host.Host.Data.GetProjects());
    }

    [Fact]
    public async Task ExpiredSessionIsRejected()
    {
        var clock = new MutableClock();
        using var host = TestHost.Create(clock);
        await host.SeedAdminAsync();

        Assert.NotNull(host.Host.Sessions.Current);

        clock.Advance(SessionStore.IdleTimeout + TimeSpan.FromMinutes(1));

        Assert.Null(host.Host.Sessions.Current);
        Assert.Throws<NotAuthenticatedException>(() => host.Host.Data.GetProjects());
    }

    [Fact]
    public async Task ForgedPrincipalCannotBeUsedWithoutStore()
    {
        using var host = TestHost.Create();
        await host.SeedAdminAsync();
        host.Host.Sessions.Clear();

        var forged = new Principal(999, "ghost", "Ghost", AppRole.Admin, "deadbeef",
            host.Clock.UtcNow, host.Clock.UtcNow.AddHours(1), false);

        Assert.False(host.Host.Authorization.Has(Permissions.UserManage));
        Assert.True(forged.Has(Permissions.UserManage));
        Assert.Throws<NotAuthenticatedException>(() => host.Host.Authorization.Require(Permissions.UserManage));
    }

    [Fact]
    public async Task DisabledAccountCannotSignIn()
    {
        using var host = TestHost.Create();
        await host.SeedAdminAsync();
        await host.SeedUserAsync("viewer", TestHost.ViewerPassword, AppRole.Viewer);

        await using (var db = host.Host.Database.Create())
        {
            var viewer = db.Users.Single(x => x.UsernameNormalized == "VIEWER");
            await host.Host.Users.SetActiveAsync(viewer.Id, false);
        }

        host.Host.Sessions.Clear();
        var result = await host.LoginAsync("viewer", TestHost.ViewerPassword);
        Assert.Equal(AuthOutcome.Disabled, result.Outcome);
    }

    [Fact]
    public async Task LastAdminCannotBeDemotedOrDisabled()
    {
        using var host = TestHost.Create();
        var adminId = await host.SeedAdminAsync();

        var demote = await host.Host.Users.SetRoleAsync(adminId, AppRole.Viewer);
        var disable = await host.Host.Users.SetActiveAsync(adminId, false);

        Assert.Equal(AuthOutcome.Conflict, demote.Outcome);
        Assert.Equal(AuthOutcome.Conflict, disable.Outcome);
    }

    [Fact]
    public async Task PasswordChangeRequiresCurrentPassword()
    {
        using var host = TestHost.Create();
        await host.SeedAdminAsync();

        var wrong = await host.Host.Authentication.ChangePasswordAsync("not-the-password", "Yangi!Parol#2026x");
        Assert.Equal(AuthOutcome.InvalidCredentials, wrong.Outcome);

        var ok = await host.Host.Authentication.ChangePasswordAsync(TestHost.AdminPassword, "Yangi!Parol#2026x");
        Assert.True(ok.Succeeded);

        host.Host.Sessions.Clear();
        Assert.True((await host.LoginAsync("admin", "Yangi!Parol#2026x")).Succeeded);
    }
}
