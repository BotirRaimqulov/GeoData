using System;
using System.Collections.Generic;
using System.Linq;

namespace GeoDataPro.Core.Security;

public static class Permissions
{
    public const string ProjectRead = "project.read";
    public const string ProjectCreate = "project.create";
    public const string ProjectUpdate = "project.update";
    public const string ProjectDelete = "project.delete";

    public const string SampleRead = "sample.read";
    public const string SampleWrite = "sample.write";
    public const string SampleDelete = "sample.delete";

    public const string ReferenceRead = "reference.read";
    public const string ReferenceWrite = "reference.write";

    public const string Export = "export";
    public const string Import = "import";
    public const string Backup = "backup";
    public const string Restore = "restore";

    public const string UserManage = "user.manage";
    public const string AuditRead = "audit.read";
    public const string SettingsManage = "settings.manage";

    public const string DataDeletePermanent = "data.delete.permanent";

    public static readonly IReadOnlyList<string> All = new[]
    {
        ProjectRead, ProjectCreate, ProjectUpdate, ProjectDelete,
        SampleRead, SampleWrite, SampleDelete,
        ReferenceRead, ReferenceWrite,
        Export, Import, Backup, Restore,
        UserManage, AuditRead, SettingsManage,
        DataDeletePermanent,
    };
}

public enum AppRole
{
    Viewer = 0,
    Operator = 1,
    Geologist = 2,
    Auditor = 3,
    Admin = 4,
}

public static class RolePermissions
{
    static readonly IReadOnlyDictionary<AppRole, HashSet<string>> Map =
        new Dictionary<AppRole, HashSet<string>>
        {
            [AppRole.Viewer] = new(StringComparer.Ordinal)
            {
                Permissions.ProjectRead, Permissions.SampleRead, Permissions.ReferenceRead,
            },
            [AppRole.Operator] = new(StringComparer.Ordinal)
            {
                Permissions.ProjectRead, Permissions.SampleRead, Permissions.SampleWrite,
                Permissions.ReferenceRead, Permissions.Export,
            },
            [AppRole.Geologist] = new(StringComparer.Ordinal)
            {
                Permissions.ProjectRead, Permissions.ProjectCreate, Permissions.ProjectUpdate,
                Permissions.SampleRead, Permissions.SampleWrite, Permissions.SampleDelete,
                Permissions.ReferenceRead, Permissions.ReferenceWrite,
                Permissions.Export, Permissions.Import, Permissions.Backup,
            },
            [AppRole.Auditor] = new(StringComparer.Ordinal)
            {
                Permissions.ProjectRead, Permissions.SampleRead, Permissions.ReferenceRead,
                Permissions.AuditRead, Permissions.Export,
            },
            [AppRole.Admin] = new(Permissions.All, StringComparer.Ordinal),
        };

    public static IReadOnlyCollection<string> For(AppRole role) =>
        Map.TryGetValue(role, out var set) ? set : Array.Empty<string>();

    public static bool Grants(AppRole role, string permission) =>
        Map.TryGetValue(role, out var set) && set.Contains(permission);

    public static bool IsDefined(AppRole role) => Map.ContainsKey(role);
}
