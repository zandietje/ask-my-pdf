namespace AskMyPdf.Infrastructure.Data;

using System.Text.Json;
using AskMyPdf.Core.Models;
using Microsoft.Data.Sqlite;

public class DocumentRepository(DbConnectionFactory db)
{
    public async Task SaveDocumentAsync(Document doc, byte[] fileBytes, List<PageCanonicalData> pages)
    {
        await using var conn = await db.OpenConnectionAsync();
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

        // Insert page data (canonical text + tokens)
        foreach (var page in pages)
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = (SqliteTransaction)tx;
            cmd.CommandText = """
                INSERT INTO page_bounds (document_id, page_number, page_width, page_height, canonical_text, tokens_json)
                VALUES (@docId, @pageNum, @width, @height, @canonicalText, @tokensJson)
                """;
            cmd.Parameters.AddWithValue("@docId", doc.Id);
            cmd.Parameters.AddWithValue("@pageNum", page.PageNumber);
            cmd.Parameters.AddWithValue("@width", page.PageWidth);
            cmd.Parameters.AddWithValue("@height", page.PageHeight);
            cmd.Parameters.AddWithValue("@canonicalText", page.CanonicalText);
            cmd.Parameters.AddWithValue("@tokensJson", JsonSerializer.Serialize(page.Tokens));
            await cmd.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
    }

    public async Task<bool> DeleteDocumentAsync(string id)
    {
        await using var conn = await db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        // Delete from child tables first, then parent
        foreach (var table in new[] { "chunk_embeddings", "document_chunks", "page_bounds", "stored_files" })
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
        await using var conn = await db.OpenConnectionAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, file_name, uploaded_at, page_count, file_size FROM documents WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);

        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadDocument(reader) : null;
    }

    public async Task<List<Document>> GetAllDocumentsAsync()
    {
        await using var conn = await db.OpenConnectionAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, file_name, uploaded_at, page_count, file_size FROM documents ORDER BY uploaded_at DESC";

        var docs = new List<Document>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            docs.Add(ReadDocument(reader));

        return docs;
    }

    public async Task<byte[]?> GetFileAsync(string documentId)
    {
        await using var conn = await db.OpenConnectionAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT file_bytes FROM stored_files WHERE document_id = @docId";
        cmd.Parameters.AddWithValue("@docId", documentId);

        var result = await cmd.ExecuteScalarAsync();
        return result as byte[];
    }

    public async Task<List<PageCanonicalData>> GetPageDataAsync(string documentId, IReadOnlyList<int> pageNumbers)
    {
        if (pageNumbers.Count == 0) return [];

        await using var conn = await db.OpenConnectionAsync();

        var paramNames = pageNumbers.Select((_, i) => $"@p{i}").ToList();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT page_number, page_width, page_height, canonical_text, tokens_json FROM page_bounds WHERE document_id = @docId AND page_number IN ({string.Join(", ", paramNames)}) ORDER BY page_number";
        cmd.Parameters.AddWithValue("@docId", documentId);
        for (var i = 0; i < pageNumbers.Count; i++)
            cmd.Parameters.AddWithValue(paramNames[i], pageNumbers[i]);

        var pages = new List<PageCanonicalData>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            pages.Add(ReadPageData(reader));

        return pages;
    }

    public async Task<List<PageCanonicalData>> GetPageDataAsync(string documentId)
    {
        await using var conn = await db.OpenConnectionAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT page_number, page_width, page_height, canonical_text, tokens_json FROM page_bounds WHERE document_id = @docId ORDER BY page_number";
        cmd.Parameters.AddWithValue("@docId", documentId);

        var pages = new List<PageCanonicalData>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            pages.Add(ReadPageData(reader));

        return pages;
    }

    private static Document ReadDocument(SqliteDataReader reader) =>
        new(reader.GetString(0),
            reader.GetString(1),
            DateTime.Parse(reader.GetString(2), null, System.Globalization.DateTimeStyles.RoundtripKind),
            reader.GetInt32(3),
            reader.GetInt64(4));

    private static PageCanonicalData ReadPageData(SqliteDataReader reader)
    {
        var tokens = JsonSerializer.Deserialize<List<PageToken>>(reader.GetString(4)) ?? [];
        return new PageCanonicalData(
            reader.GetInt32(0),
            reader.GetDouble(1),
            reader.GetDouble(2),
            reader.GetString(3),
            tokens);
    }
}
