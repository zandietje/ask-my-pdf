namespace AskMyPdf.Infrastructure.Data;

using System.Text.Json;
using AskMyPdf.Core.Models;
using Microsoft.Data.Sqlite;

public class SqliteDb(string dbPath)
{
    private readonly string _connectionString = $"Data Source={dbPath}";

    public async Task InitializeAsync()
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

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
                words_json TEXT NOT NULL,
                PRIMARY KEY (document_id, page_number)
            );
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task SaveDocumentAsync(Document doc, byte[] fileBytes, List<PageBoundingData> bounds)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        // Insert document metadata
        await using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = (SqliteTransaction)tx;
            cmd.CommandText = """
                INSERT INTO documents (id, file_name, uploaded_at, page_count, file_size)
                VALUES (@id, @fileName, @uploadedAt, @pageCount, @fileSize)
                """;
            cmd.Parameters.AddWithValue("@id", doc.Id);
            cmd.Parameters.AddWithValue("@fileName", doc.FileName);
            cmd.Parameters.AddWithValue("@uploadedAt", doc.UploadedAt.ToString("O"));
            cmd.Parameters.AddWithValue("@pageCount", doc.PageCount);
            cmd.Parameters.AddWithValue("@fileSize", doc.FileSize);
            await cmd.ExecuteNonQueryAsync();
        }

        // Insert file bytes
        await using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = (SqliteTransaction)tx;
            cmd.CommandText = "INSERT INTO stored_files (document_id, file_bytes) VALUES (@docId, @bytes)";
            cmd.Parameters.AddWithValue("@docId", doc.Id);
            cmd.Parameters.AddWithValue("@bytes", fileBytes);
            await cmd.ExecuteNonQueryAsync();
        }

        // Insert page bounds
        foreach (var page in bounds)
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = (SqliteTransaction)tx;
            cmd.CommandText = """
                INSERT INTO page_bounds (document_id, page_number, page_width, page_height, words_json)
                VALUES (@docId, @pageNum, @width, @height, @wordsJson)
                """;
            cmd.Parameters.AddWithValue("@docId", doc.Id);
            cmd.Parameters.AddWithValue("@pageNum", page.PageNumber);
            cmd.Parameters.AddWithValue("@width", page.PageWidth);
            cmd.Parameters.AddWithValue("@height", page.PageHeight);
            cmd.Parameters.AddWithValue("@wordsJson", JsonSerializer.Serialize(page.Words));
            await cmd.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
    }

    public async Task<bool> DeleteDocumentAsync(string id)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        // Delete from child tables first, then parent
        foreach (var table in new[] { "page_bounds", "stored_files" })
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = (SqliteTransaction)tx;
            cmd.CommandText = $"DELETE FROM {table} WHERE document_id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            await cmd.ExecuteNonQueryAsync();
        }

        int rows;
        await using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = (SqliteTransaction)tx;
            cmd.CommandText = "DELETE FROM documents WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            rows = await cmd.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
        return rows > 0;
    }

    public async Task<Document?> GetDocumentAsync(string id)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, file_name, uploaded_at, page_count, file_size FROM documents WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        return new Document(
            reader.GetString(0),
            reader.GetString(1),
            DateTime.Parse(reader.GetString(2), null, System.Globalization.DateTimeStyles.RoundtripKind),
            reader.GetInt32(3),
            reader.GetInt64(4));
    }

    public async Task<List<Document>> GetAllDocumentsAsync()
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, file_name, uploaded_at, page_count, file_size FROM documents ORDER BY uploaded_at DESC";

        var docs = new List<Document>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            docs.Add(new Document(
                reader.GetString(0),
                reader.GetString(1),
                DateTime.Parse(reader.GetString(2), null, System.Globalization.DateTimeStyles.RoundtripKind),
                reader.GetInt32(3),
                reader.GetInt64(4)));
        }

        return docs;
    }

    public async Task<byte[]?> GetFileAsync(string documentId)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT file_bytes FROM stored_files WHERE document_id = @docId";
        cmd.Parameters.AddWithValue("@docId", documentId);

        var result = await cmd.ExecuteScalarAsync();
        return result as byte[];
    }

    public async Task<List<PageBoundingData>> GetPageBoundsAsync(string documentId)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT page_number, page_width, page_height, words_json FROM page_bounds WHERE document_id = @docId ORDER BY page_number";
        cmd.Parameters.AddWithValue("@docId", documentId);

        var pages = new List<PageBoundingData>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var words = JsonSerializer.Deserialize<List<WordBoundingBox>>(reader.GetString(3)) ?? [];
            pages.Add(new PageBoundingData(
                reader.GetInt32(0),
                reader.GetDouble(1),
                reader.GetDouble(2),
                words));
        }

        return pages;
    }
}
