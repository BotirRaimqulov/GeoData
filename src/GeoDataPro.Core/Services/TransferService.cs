using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GeoDataPro.Core.Audit;
using GeoDataPro.Core.Backup;
using GeoDataPro.Core.Data;
using GeoDataPro.Core.Files;
using GeoDataPro.Core.Security;

namespace GeoDataPro.Core.Services;

public interface ITransferService
{
    Task<ExcelService.ImportResult> ImportAsync(string path, int? projectId, string? newProjectName, CancellationToken ct = default);
    Task ExportProjectAsync(string path, int projectId, IEnumerable<int>? wellIds = null, CancellationToken ct = default);
    Task ExportWellAsync(string path, int wellId, CancellationToken ct = default);
}

public sealed class TransferService : ITransferService
{
    readonly IDbContextFactory _factory;
    readonly IAuthorizationService _authz;
    readonly IAuditService _audit;
    readonly IBackupService _backup;

    public TransferService(
        IDbContextFactory factory,
        IAuthorizationService authz,
        IAuditService audit,
        IBackupService backup)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _authz = authz ?? throw new ArgumentNullException(nameof(authz));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _backup = backup ?? throw new ArgumentNullException(nameof(backup));
    }

    public async Task<ExcelService.ImportResult> ImportAsync(
        string path, int? projectId, string? newProjectName, CancellationToken ct = default)
    {
        var actor = _authz.Require(Permissions.Import);
        SafeFile.ValidateWorkbook(path);

        if (_authz.Has(Permissions.Backup))
        {
            try
            {
                await _backup.CreateAsync(ct).ConfigureAwait(false);
            }
            catch (Exception)
            {
            }
        }

        try
        {
            var result = await Task.Run(
                () => ExcelService.ImportWorkbookToProject(path, projectId, newProjectName, ct), ct)
                .ConfigureAwait(false);

            await _audit.WriteAsync(new AuditRecord(AuditActions.Import, AuditResults.Success, "Workbook",
                result.ProjectName, result.WellNumbers.Count.ToString(CultureInfo.InvariantCulture),
                actor.UserId, actor.Username), ct).ConfigureAwait(false);

            return result;
        }
        catch (Exception ex)
        {
            await _audit.WriteAsync(new AuditRecord(AuditActions.Import, AuditResults.Failure, "Workbook",
                null, ex.GetType().Name, actor.UserId, actor.Username), ct).ConfigureAwait(false);
            throw;
        }
    }

    public async Task ExportProjectAsync(string path, int projectId, IEnumerable<int>? wellIds = null, CancellationToken ct = default)
    {
        var actor = _authz.Require(Permissions.Export);
        var destination = ValidateDestination(path);

        try
        {
            await Task.Run(() => SafeFile.WriteAtomic(destination,
                temp => ExcelService.ExportWorkbookMulti(temp, projectId, wellIds, ct)), ct).ConfigureAwait(false);

            await _audit.WriteAsync(new AuditRecord(AuditActions.Export, AuditResults.Success, "Project",
                projectId.ToString(CultureInfo.InvariantCulture), null, actor.UserId, actor.Username), ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await _audit.WriteAsync(new AuditRecord(AuditActions.Export, AuditResults.Failure, "Project",
                projectId.ToString(CultureInfo.InvariantCulture), ex.GetType().Name, actor.UserId, actor.Username), ct)
                .ConfigureAwait(false);
            throw;
        }
    }

    public async Task ExportWellAsync(string path, int wellId, CancellationToken ct = default)
    {
        var actor = _authz.Require(Permissions.Export);
        var destination = ValidateDestination(path);

        Well well;
        await using (var db = _factory.Create())
        {
            well = db.Wells.FirstOrDefault(x => x.Id == wellId && !x.IsDeleted)
                ?? throw new InvalidOperationException("E_NOTFOUND");
        }

        try
        {
            await Task.Run(() => SafeFile.WriteAtomic(destination,
                temp => ExcelService.ExportWorkbook(temp, well, ct)), ct).ConfigureAwait(false);

            await _audit.WriteAsync(new AuditRecord(AuditActions.Export, AuditResults.Success, "Well",
                wellId.ToString(CultureInfo.InvariantCulture), null, actor.UserId, actor.Username), ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await _audit.WriteAsync(new AuditRecord(AuditActions.Export, AuditResults.Failure, "Well",
                wellId.ToString(CultureInfo.InvariantCulture), ex.GetType().Name, actor.UserId, actor.Username), ct)
                .ConfigureAwait(false);
            throw;
        }
    }

    static string ValidateDestination(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new FileGuardException(FileRejection.Missing);
        if (path.IndexOf('\0') >= 0) throw new FileGuardException(FileRejection.Traversal);

        string full;
        try { full = Path.GetFullPath(path); }
        catch (Exception) { throw new FileGuardException(FileRejection.Traversal); }

        if (!string.Equals(Path.GetExtension(full), ".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new FileGuardException(FileRejection.BadExtension);

        var directory = Path.GetDirectoryName(full);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            throw new FileGuardException(FileRejection.OutsideRoot);

        var name = Path.GetFileName(full);
        if (!string.Equals(name, SafeFile.SanitizeFileName(name), StringComparison.Ordinal))
            throw new FileGuardException(FileRejection.Traversal);

        return full;
    }
}
