using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RideMatching.Api.Data;

namespace RideMatching.Tests;

/// <summary>
/// A real relational (SQLite) database backing the matching tests. SQLite executes
/// genuine SQL including the atomic conditional UPDATE used for driver claims, so
/// the concurrency behaviour is exercised for real rather than mocked.
///
/// Each test gets its own on-disk SQLite file and every <see cref="CreateContext"/>
/// call opens an independent physical connection to it. That means each competing
/// matching operation runs its own genuinely-independent transaction — exactly the
/// isolation two real web requests / worker invocations have against SQL Server —
/// rather than sharing one connection (which SQLite forbids nesting transactions on).
///
/// A busy_timeout is applied so that a writer contending for the lock waits for the
/// other transaction to commit instead of failing fast with SQLITE_BUSY, mirroring
/// SQL Server's row-lock waiting behaviour.
/// </summary>
public sealed class SqliteTestDatabase : IDisposable
{
    private readonly string _dbPath;
    private readonly string _connectionString;

    public SqliteTestDatabase()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"ridematch-tests-{Guid.NewGuid():N}.db");
        _connectionString = $"Data Source={_dbPath}";

        using var ctx = CreateContext();
        ctx.Database.EnsureCreated();
    }

    public AppDbContext CreateContext()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using (var pragma = connection.CreateCommand())
        {
            // Wait up to 5s for a competing write lock rather than failing fast.
            pragma.CommandText = "PRAGMA busy_timeout = 5000;";
            pragma.ExecuteNonQuery();
        }

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection, o => o.CommandTimeout(30))
            .Options;

        // Context owns the connection and disposes it when the context is disposed.
        return new OwningAppDbContext(options, connection);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup of the temp file.
        }
    }

    /// <summary>AppDbContext that also disposes the SQLite connection it was given.</summary>
    private sealed class OwningAppDbContext : AppDbContext
    {
        private readonly SqliteConnection _connection;

        public OwningAppDbContext(DbContextOptions<AppDbContext> options, SqliteConnection connection)
            : base(options) => _connection = connection;

        public override void Dispose()
        {
            base.Dispose();
            _connection.Dispose();
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
