using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using GeoDataPro.Core.Data;
using GeoDataPro.Core.Security;
using GeoDataPro.Core.Services;
using Xunit;

namespace GeoDataPro.Tests;

public class AuthorizationTests
{
    [Fact]
    public async Task ViewerCannotWriteEvenWhenUiWouldAllowIt()
    {
        using var host = TestHost.Create();
        await host.SeedAdminAsync();
        var project = host.Host.Data.CreateProject("Loyiha-01");
        var well = host.Host.Data.CreateWell(project.Id, "1001");

        await host.SeedUserAsync("viewer", TestHost.ViewerPassword, AppRole.Viewer);
        host.Host.Sessions.Clear();
        Assert.True((await host.LoginAsync("viewer", TestHost.ViewerPassword)).Succeeded);

        Assert.NotEmpty(host.Host.Data.GetProjects());

        Assert.Throws<SecurityDeniedException>(() => host.Host.Data.CreateProject("Yashirin"));
        Assert.Throws<SecurityDeniedException>(() => host.Host.Data.CreateWell(project.Id, "9999"));
        Assert.Throws<SecurityDeniedException>(() => host.Host.Data.SaveJournal(well.Id, new List<JournalRow>()));
        Assert.Throws<SecurityDeniedException>(() => host.Host.Data.SaveSamples(well.Id, new List<SampleRow>()));
        Assert.Throws<SecurityDeniedException>(() => host.Host.Data.SaveSrp(well.Id, new List<SrpRow>()));
        Assert.Throws<SecurityDeniedException>(() => host.Host.Data.SoftDeleteWell(well.Id));
        Assert.Throws<SecurityDeniedException>(() => host.Host.Data.PurgeWell(well.Id));
        Assert.Throws<SecurityDeniedException>(() =>
            host.Host.Data.SaveReferences(ReferenceKind.Litho, new List<object>()));
    }

    [Fact]
    public async Task OperatorCannotEscalateOwnRole()
    {
        using var host = TestHost.Create();
        await host.SeedAdminAsync();
        await host.SeedUserAsync("operator", TestHost.OperatorPassword, AppRole.Operator);

        int operatorId;
        await using (var db = host.Host.Database.Create())
            operatorId = db.Users.Single(x => x.UsernameNormalized == "OPERATOR").Id;

        host.Host.Sessions.Clear();
        Assert.True((await host.LoginAsync("operator", TestHost.OperatorPassword)).Succeeded);

        await Assert.ThrowsAsync<SecurityDeniedException>(
            () => host.Host.Users.SetRoleAsync(operatorId, AppRole.Admin));
        await Assert.ThrowsAsync<SecurityDeniedException>(
            () => host.Host.Users.CreateAsync("mole", "Mole", TestHost.AdminPassword, AppRole.Admin));
        await Assert.ThrowsAsync<SecurityDeniedException>(() => host.Host.Users.ListAsync());

        await using (var db = host.Host.Database.Create())
            Assert.Equal((int)AppRole.Operator, db.Users.Single(x => x.Id == operatorId).Role);
    }

    [Fact]
    public async Task PermanentDeleteRequiresDedicatedPermission()
    {
        using var host = TestHost.Create();
        await host.SeedAdminAsync();
        var project = host.Host.Data.CreateProject("Loyiha-02");
        var well = host.Host.Data.CreateWell(project.Id, "2002");

        await host.SeedUserAsync("geolog", "Kyzylkum!2026#Geo", AppRole.Geologist);
        host.Host.Sessions.Clear();
        Assert.True((await host.LoginAsync("geolog", "Kyzylkum!2026#Geo")).Succeeded);

        Assert.False(host.Host.Authorization.Has(Permissions.DataDeletePermanent));
        Assert.Throws<SecurityDeniedException>(() => host.Host.Data.PurgeWell(well.Id));

        await using var db = host.Host.Database.Create();
        Assert.True(db.Wells.Any(x => x.Id == well.Id));
    }

    [Fact]
    public async Task ExportAndImportAreGated()
    {
        using var host = TestHost.Create();
        await host.SeedAdminAsync();
        var project = host.Host.Data.CreateProject("Loyiha-03");
        host.Host.Data.CreateWell(project.Id, "3003");

        await host.SeedUserAsync("viewer", TestHost.ViewerPassword, AppRole.Viewer);
        host.Host.Sessions.Clear();
        await host.LoginAsync("viewer", TestHost.ViewerPassword);

        var target = System.IO.Path.Combine(host.Root, "leak.xlsx");
        await Assert.ThrowsAsync<SecurityDeniedException>(
            () => host.Host.Transfer.ExportProjectAsync(target, project.Id));
        await Assert.ThrowsAsync<SecurityDeniedException>(
            () => host.Host.Transfer.ImportAsync(target, project.Id, null));

        Assert.False(System.IO.File.Exists(target));
    }

    [Fact]
    public async Task BackupAndRestoreAreGated()
    {
        using var host = TestHost.Create();
        await host.SeedAdminAsync();
        await host.SeedUserAsync("operator", TestHost.OperatorPassword, AppRole.Operator);
        host.Host.Sessions.Clear();
        await host.LoginAsync("operator", TestHost.OperatorPassword);

        await Assert.ThrowsAsync<SecurityDeniedException>(() => host.Host.Backup.CreateAsync());
        await Assert.ThrowsAsync<SecurityDeniedException>(() => host.Host.Backup.RestoreAsync("anything.gdb"));
        Assert.Throws<SecurityDeniedException>(() => host.Host.Backup.List());
    }

    [Fact]
    public void RoleMatrixGrantsExactlyWhatItDeclares()
    {
        Assert.True(RolePermissions.Grants(AppRole.Admin, Permissions.DataDeletePermanent));
        Assert.True(RolePermissions.Grants(AppRole.Admin, Permissions.UserManage));

        Assert.False(RolePermissions.Grants(AppRole.Geologist, Permissions.DataDeletePermanent));
        Assert.False(RolePermissions.Grants(AppRole.Geologist, Permissions.UserManage));
        Assert.False(RolePermissions.Grants(AppRole.Auditor, Permissions.SampleWrite));
        Assert.False(RolePermissions.Grants(AppRole.Operator, Permissions.ProjectDelete));
        Assert.False(RolePermissions.Grants(AppRole.Viewer, Permissions.Export));

        Assert.True(RolePermissions.Grants(AppRole.Auditor, Permissions.AuditRead));
        Assert.False(RolePermissions.Grants(AppRole.Geologist, Permissions.AuditRead));

        Assert.False(RolePermissions.IsDefined((AppRole)77));
    }

    [Fact]
    public async Task UndefinedRoleIsRejectedByDatabaseAndByPolicy()
    {
        using var host = TestHost.Create();
        await host.SeedAdminAsync();
        await host.SeedUserAsync("victim", "Chimyon!2026#Usr", AppRole.Operator);

        await using (var db = host.Host.Database.Create())
        {
            var user = db.Users.Single(x => x.UsernameNormalized == "VICTIM");

            var blocked = Record.Exception(() =>
                db.Database.ExecuteSqlRaw("UPDATE Users SET Role = 99 WHERE Id = {0}", user.Id));

            Assert.NotNull(blocked);
            Assert.Contains("CK_Users_Role", blocked!.ToString(), StringComparison.Ordinal);
            Assert.Equal((int)AppRole.Operator,
                db.Users.AsNoTracking().Single(x => x.Id == user.Id).Role);
        }

        Assert.False(RolePermissions.IsDefined((AppRole)99));
        foreach (var permission in Permissions.All)
            Assert.False(RolePermissions.Grants((AppRole)99, permission));

        var promotion = await host.Host.Users.SetRoleAsync(
            (await host.Host.Users.ListAsync()).Single(x => x.Username == "victim").Id, (AppRole)99);
        Assert.Equal(AuthOutcome.InvalidCredentials, promotion.Outcome);
    }

    [Fact]
    public async Task WriteAcrossWellBoundaryIsRefused()
    {
        using var host = TestHost.Create();
        await host.SeedAdminAsync();
        var project = host.Host.Data.CreateProject("Loyiha-04");
        var wellA = host.Host.Data.CreateWell(project.Id, "A-1");
        var wellB = host.Host.Data.CreateWell(project.Id, "B-1");

        host.Host.Data.SaveJournal(wellA.Id, new List<JournalRow>
        {
            new() { Top = 0, Bottom = 5, CoreRecoveryM = 4, ZoneName = "1" },
        });

        var stolen = host.Host.Data.GetJournalRows(wellA.Id).Single();

        var refusal = Record.Exception(() =>
            host.Host.Data.SaveJournal(wellB.Id, new List<JournalRow> { stolen }));

        Assert.NotNull(refusal);
        Assert.Empty(host.Host.Data.GetJournalRows(wellB.Id));

        var original = host.Host.Data.GetJournalRows(wellA.Id).Single();
        Assert.Equal(wellA.Id, original.WellId);
        Assert.Equal(stolen.Id, original.Id);
    }
}
