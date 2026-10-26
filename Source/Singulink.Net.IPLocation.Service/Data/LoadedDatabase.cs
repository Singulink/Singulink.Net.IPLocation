using MaxMind.Db;

namespace Singulink.Net.IPLocation.Service.Data;

/// <summary>
/// An open database file. The file is memory-mapped, so it stays locked until the database is disposed.
/// </summary>
public sealed class LoadedDatabase : IDisposable
{
    private LoadedDatabase(DbIpDatabaseKind kind, DateOnly month, string path, Reader reader)
    {
        Kind = kind;
        Month = month;
        Path = path;
        Reader = reader;
    }

    public DbIpDatabaseKind Kind { get; }

    public DateOnly Month { get; }

    public string Path { get; }

    public DateTime BuildDateUtc => Reader.Metadata.BuildDate;

    public string DatabaseType => Reader.Metadata.DatabaseType;

    internal Reader Reader { get; }

    /// <summary>
    /// Opens a database file and checks that it is the expected kind of database.
    /// </summary>
    /// <exception cref="InvalidDatabaseException">The file is not a valid database of that kind.</exception>
    public static LoadedDatabase Open(DbIpDatabaseKind kind, DateOnly month, string path)
    {
        var reader = new Reader(path, FileAccessMode.MemoryMapped);
        string expectedType = DbIpFiles.GetDatabaseType(kind);

        if (!reader.Metadata.DatabaseType.Contains(expectedType, StringComparison.OrdinalIgnoreCase))
        {
            string actualType = reader.Metadata.DatabaseType;
            reader.Dispose();

            throw new InvalidDatabaseException($"Expected a {expectedType} database but the file contains '{actualType}'.");
        }

        return new LoadedDatabase(kind, month, path, reader);
    }

    public void Dispose() => Reader.Dispose();
}
