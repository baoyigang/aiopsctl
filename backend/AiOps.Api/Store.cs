using Microsoft.Data.Sqlite;
using System.Text.Json;
namespace AiOps;

public sealed class Store
{
    readonly string connectionString; readonly object gate = new();
    public Store(string path) { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!); connectionString = new SqliteConnectionStringBuilder { DataSource = path }.ToString(); using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "PRAGMA journal_mode=WAL; CREATE TABLE IF NOT EXISTS documents (space TEXT NOT NULL, kind TEXT NOT NULL, id TEXT NOT NULL, body TEXT NOT NULL, PRIMARY KEY(space,kind,id)); PRAGMA user_version=1;"; cmd.ExecuteNonQuery(); }
    SqliteConnection Open() { var c = new SqliteConnection(connectionString); c.Open(); return c; }
    public T[] List<T>(string space, string kind) { lock (gate) { using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT body FROM documents WHERE space=$s AND kind=$k ORDER BY rowid DESC LIMIT 10000"; cmd.Parameters.AddWithValue("$s", space); cmd.Parameters.AddWithValue("$k", kind); using var r = cmd.ExecuteReader(); var list = new List<T>(); while (r.Read()) list.Add(JsonSerializer.Deserialize<T>(r.GetString(0))!); return list.ToArray(); } }
    public void Put<T>(string space, string kind, string id, T value, bool audit = true) { lock (gate) { using var c = Open(); using var tx = c.BeginTransaction(); Write(c, tx, space, kind, id, value); if (audit) { var a = new Audit(Guid.NewGuid().ToString("N"), "保存 " + kind, id, DateTimeOffset.UtcNow); Write(c, tx, space, "audit", a.Id, a); } tx.Commit(); } }
    static void Write<T>(SqliteConnection c, SqliteTransaction tx, string s, string k, string id, T value) { using var cmd = c.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = "INSERT INTO documents VALUES($s,$k,$i,$b) ON CONFLICT(space,kind,id) DO UPDATE SET body=$b"; cmd.Parameters.AddWithValue("$s", s); cmd.Parameters.AddWithValue("$k", k); cmd.Parameters.AddWithValue("$i", id); cmd.Parameters.AddWithValue("$b", JsonSerializer.Serialize(value)); cmd.ExecuteNonQuery(); }
    public bool Delete(string s, string k, string id) { lock (gate) { using var c = Open(); using var tx = c.BeginTransaction(); using var cmd = c.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = "DELETE FROM documents WHERE space=$s AND kind=$k AND id=$i"; cmd.Parameters.AddWithValue("$s", s); cmd.Parameters.AddWithValue("$k", k); cmd.Parameters.AddWithValue("$i", id); var n = cmd.ExecuteNonQuery(); var a = new Audit(Guid.NewGuid().ToString("N"), "删除 " + k, id, DateTimeOffset.UtcNow); Write(c, tx, s, "audit", a.Id, a); tx.Commit(); return n > 0; } }
    public void Atomic(Action action) { lock (gate) action(); }
    public Settings Config(string s) => List<Settings>(s, "settings").FirstOrDefault() ?? new();
}
