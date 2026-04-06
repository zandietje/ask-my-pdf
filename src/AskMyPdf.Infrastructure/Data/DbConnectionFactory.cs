namespace AskMyPdf.Infrastructure.Data;

using Microsoft.Data.Sqlite;

public class DbConnectionFactory(string dbPath)
{
    private readonly string _connectionString = $"Data Source={dbPath}";

    public async Task<SqliteConnection> OpenConnectionAsync()
    {
        var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        return conn;
    }

    public async Task InitializeAsync()
    {
        await using var conn = await OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS documents (
                id TEXT PRIMARY KEY,
                file_name TEXT NOT NULL,
                uploaded_at TEXT NOT NULL,
                page_count INTEGER NOT NULL,
                file_size INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS stored_files (
                document_id TEXT PRIMARY KEY REFERENCES documents(id),
                file_bytes BLOB NOT NULL
            );

            CREATE TABLE IF NOT EXISTS page_bounds (
                document_id TEXT NOT NULL REFERENCES documents(id),
                page_number INTEGER NOT NULL,
                page_width REAL NOT NULL,
                page_height REAL NOT NULL,
                canonical_text TEXT NOT NULL,
                tokens_json TEXT NOT NULL,
                PRIMARY KEY (document_id, page_number)
            );

            CREATE TABLE IF NOT EXISTS document_chunks (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                document_id TEXT NOT NULL REFERENCES documents(id),
                page_number INTEGER NOT NULL,
                chunk_index INTEGER NOT NULL,
                chunk_text TEXT NOT NULL,
                UNIQUE(document_id, chunk_index)
            );

            CREATE VIRTUAL TABLE IF NOT EXISTS document_chunks_fts USING fts5(
                chunk_text,
                content='document_chunks',
                content_rowid='id',
                tokenize='porter unicode61'
            );

            CREATE TRIGGER IF NOT EXISTS chunks_ai AFTER INSERT ON document_chunks BEGIN
                INSERT INTO document_chunks_fts(rowid, chunk_text) VALUES (new.id, new.chunk_text);
            END;

            CREATE TRIGGER IF NOT EXISTS chunks_ad AFTER DELETE ON document_chunks BEGIN
                INSERT INTO document_chunks_fts(document_chunks_fts, rowid, chunk_text)
                VALUES('delete', old.id, old.chunk_text);
            END;

            CREATE TABLE IF NOT EXISTS chunk_embeddings (
                document_id TEXT NOT NULL REFERENCES documents(id),
                chunk_index INTEGER NOT NULL,
                embedding BLOB NOT NULL,
                PRIMARY KEY (document_id, chunk_index)
            );
            """;
        await cmd.ExecuteNonQueryAsync();
    }
}
