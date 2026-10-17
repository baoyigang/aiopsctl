using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

namespace Hengchuan.Api;

/// <summary>
/// Schema 1 stores one complete configuration snapshot. Revision is the optimistic write token.
/// A future normalized schema can migrate the JSON inside one transaction without changing API DTOs.
/// No mutable snapshot is cached or shared across reads. Microsoft.Data.Sqlite's async API uses
/// synchronous SQLite operations; busy transaction retries use cancellable asynchronous delays.
/// </summary>
public sealed class AtlasStore : IAsyncDisposable, IDisposable
{
    private readonly string connectionString;
    private readonly SemaphoreSlim gate = new(1, 1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 32
    };

    public AtlasStore(string dbPath)
    {
        if (string.IsNullOrWhiteSpace(dbPath)) throw new ArgumentException("必须配置 SQLite 文件路径", nameof(dbPath));
        var path = Path.GetFullPath(dbPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate,
            // SqliteCommand interprets 0 as an unlimited wait. Bound each native lock wait,
            // then recheck cancellation before the asynchronous retry delay.
            DefaultTimeout = 1, ForeignKeys = true
        }.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using (var wal = connection.CreateCommand())
            {
                wal.CommandText = "PRAGMA journal_mode=WAL;";
                await RetryBusyAsync(() => wal.ExecuteNonQueryAsync(cancellationToken), cancellationToken);
            }
            await using var transaction = await BeginWriteAsync(connection, cancellationToken);
            var version = await UserVersionAsync(connection, transaction, cancellationToken);
            if (version > 1) throw new AtlasException(500, "UNSUPPORTED_SCHEMA", "SQLite 数据版本较新，程序不会自动降级");
            await using var schema = connection.CreateCommand();
            schema.Transaction = transaction;
            schema.CommandText = """
                CREATE TABLE IF NOT EXISTS atlas_snapshot (
                    id INTEGER PRIMARY KEY CHECK (id = 1),
                    schema_version INTEGER NOT NULL,
                    revision INTEGER NOT NULL CHECK (revision > 0),
                    payload TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );
                PRAGMA user_version=1;
                """;
            await schema.ExecuteNonQueryAsync(cancellationToken);
            await using var count = connection.CreateCommand();
            count.Transaction = transaction;
            count.CommandText = "SELECT COUNT(*) FROM atlas_snapshot";
            var rowCount = Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken));
            if (rowCount == 0)
            {
                var seed = DemoSeed.Create(DateTimeOffset.UtcNow);
                AtlasValidation.Validate(seed);
                await using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = "INSERT INTO atlas_snapshot(id,schema_version,revision,payload,updated_at) VALUES(1,@schema,1,@payload,@at)";
                insert.Parameters.AddWithValue("@schema", seed.SchemaVersion);
                insert.Parameters.AddWithValue("@payload", JsonSerializer.Serialize(seed, Json));
                insert.Parameters.AddWithValue("@at", DateTimeOffset.UtcNow.ToString("O"));
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }
            else
            {
                _ = await ReadRowAsync(connection, transaction, cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            await transaction.CommitAsync(cancellationToken);
        }
        finally { gate.Release(); }
    }

    public async Task<StoreSnapshot> ReadAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            return await ReadRowAsync(connection, null, cancellationToken);
        }
        finally { gate.Release(); }
    }

    public async Task<StoreSnapshot> UpdateAsync(long expectedRevision, Action<AtlasState> mutate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        if (expectedRevision < 1) throw new AtlasException(400, "VALIDATION_FAILED", "revision 必须为正整数");
        await gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var transaction = await BeginWriteAsync(connection, cancellationToken);
            var current = await ReadRowAsync(connection, transaction, cancellationToken);
            if (current.Revision != expectedRevision)
                throw new AtlasException(409, "REVISION_CONFLICT", "配置已被其他操作更新，请刷新后重新编辑");
            var candidate = Clone(current.State);
            cancellationToken.ThrowIfCancellationRequested();
            mutate(candidate);
            cancellationToken.ThrowIfCancellationRequested();
            AtlasValidation.ValidateDeletions(current.State, candidate);
            AtlasValidation.Validate(candidate);
            if (current.Revision == long.MaxValue) throw new AtlasException(409, "REVISION_EXHAUSTED", "配置版本已达上限");
            var revision = current.Revision + 1;
            var payload = JsonSerializer.Serialize(candidate, Json);
            cancellationToken.ThrowIfCancellationRequested();
            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = "UPDATE atlas_snapshot SET schema_version=@schema,revision=@next,payload=@payload,updated_at=@at WHERE id=1 AND revision=@expected";
            update.Parameters.AddWithValue("@schema", candidate.SchemaVersion);
            update.Parameters.AddWithValue("@next", revision);
            update.Parameters.AddWithValue("@payload", payload);
            update.Parameters.AddWithValue("@at", DateTimeOffset.UtcNow.ToString("O"));
            update.Parameters.AddWithValue("@expected", expectedRevision);
            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new AtlasException(409, "REVISION_CONFLICT", "配置已更新，请刷新后重试");
            cancellationToken.ThrowIfCancellationRequested();
            await transaction.CommitAsync(cancellationToken);
            return new StoreSnapshot(revision, Clone(candidate));
        }
        finally { gate.Release(); }
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(connectionString);
        try { await connection.OpenAsync(cancellationToken); return connection; }
        catch { await connection.DisposeAsync(); throw; }
    }

    private static async Task<SqliteTransaction> BeginWriteAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { return connection.BeginTransaction(deferred: false); }
            catch (SqliteException exception) when (exception.SqliteErrorCode is 5 or 6)
            {
                if (elapsed.Elapsed >= TimeSpan.FromSeconds(10)) throw new AtlasException(503, "STORE_BUSY", "配置存储繁忙，请稍后重试");
                await Task.Delay(25, cancellationToken);
            }
        }
    }

    private static async Task RetryBusyAsync(Func<Task<int>> operation, CancellationToken cancellationToken)
    {
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { await operation(); return; }
            catch (SqliteException exception) when (exception.SqliteErrorCode is 5 or 6)
            {
                if (elapsed.Elapsed >= TimeSpan.FromSeconds(10)) throw new AtlasException(503, "STORE_BUSY", "配置存储繁忙，请稍后重试");
                await Task.Delay(25, cancellationToken);
            }
        }
    }

    private static async Task<int> UserVersionAsync(SqliteConnection connection, SqliteTransaction? transaction, CancellationToken cancellationToken)
    {
        await using var version = connection.CreateCommand();
        version.Transaction = transaction;
        version.CommandText = "PRAGMA user_version";
        return Convert.ToInt32(await version.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<StoreSnapshot> ReadRowAsync(SqliteConnection connection, SqliteTransaction? transaction, CancellationToken cancellationToken)
    {
        var databaseVersion = await UserVersionAsync(connection, transaction, cancellationToken);
        if (databaseVersion > 1) throw new AtlasException(500, "UNSUPPORTED_SCHEMA", "SQLite 数据版本较新，程序不会自动降级");
        if (databaseVersion != 1) throw new AtlasException(500, "STORE_NOT_INITIALIZED", "配置存储尚未初始化");
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT schema_version,revision,payload FROM atlas_snapshot WHERE id=1";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw new AtlasException(500, "STORE_CORRUPT", "配置快照不存在");
        var schemaVersion = reader.GetInt32(0);
        if (schemaVersion > 1) throw new AtlasException(500, "UNSUPPORTED_SCHEMA", "配置快照版本较新，程序不会自动降级");
        var revision = reader.GetInt64(1);
        if (schemaVersion != 1 || revision < 1) throw new AtlasException(500, "STORE_CORRUPT", "配置快照元数据无效");
        AtlasState state;
        try
        {
            state = JsonSerializer.Deserialize<AtlasState>(reader.GetString(2), Json) ?? throw new JsonException();
            if (state.SchemaVersion > 1) throw new AtlasException(500, "UNSUPPORTED_SCHEMA", "配置内容版本较新，程序不会自动降级");
            if (state.SchemaVersion != schemaVersion) throw new JsonException();
            AtlasValidation.Validate(state);
        }
        catch (JsonException) { throw new AtlasException(500, "STORE_CORRUPT", "配置内容格式无效，不会自动覆盖"); }
        catch (AtlasException exception) when (exception.StatusCode != 500)
        { throw new AtlasException(500, "STORE_CORRUPT", "配置内容校验失败，不会自动覆盖"); }
        cancellationToken.ThrowIfCancellationRequested();
        return new StoreSnapshot(revision, state);
    }

    private static AtlasState Clone(AtlasState state) => JsonSerializer.Deserialize<AtlasState>(JsonSerializer.Serialize(state, Json), Json)!;
    public void Dispose() { }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
