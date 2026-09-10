using System;

namespace GeoDataPro.Core.Data;

public interface IDbContextFactory
{
    AppDbContext Create();
}

public interface IDatabaseProvider : IDbContextFactory
{
    string Kind { get; }
    bool SupportsLocalFile { get; }
    void EnsureReady();
}

public sealed class SqliteDatabaseProvider : IDatabaseProvider
{
    readonly Func<string> _connectionString;

    public SqliteDatabaseProvider(Func<string>? connectionString = null) =>
        _connectionString = connectionString ?? DatabaseLocation.BuildConnectionString;

    public string Kind => "sqlite";

    public bool SupportsLocalFile => true;

    public AppDbContext Create() => new(_connectionString());

    public void EnsureReady()
    {
        using var db = Create();
        db.EnsureSeeded();
    }
}
