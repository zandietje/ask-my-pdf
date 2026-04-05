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

    public async Task<List<PageBoundingData>> GetPageBoundsAsync(string documentId, IReadOnlyList<int> pageNumbers)
    {
        if (pageNumbers.Count == 0) return [];

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        var paramNames = pageNumbers.Select((_, i) => $"@p{i}").ToList();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT page_number, page_width, page_height, words_json FROM page_bounds WHERE document_id = @docId AND page_number IN ({string.Join(", ", paramNames)}) ORDER BY page_number";
        cmd.Parameters.AddWithValue("@docId", documentId);
        for (var i = 0; i < pageNumbers.Count; i++)
            cmd.Parameters.AddWithValue(paramNames[i], pageNumbers[i]);

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

    public async Task SaveChunksAsync(string documentId, List<DocumentChunk> chunks)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        foreach (var chunk in chunks)
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = (SqliteTransaction)tx;
            cmd.CommandText = """
                INSERT INTO document_chunks (document_id, page_number, chunk_index, chunk_text)
                VALUES (@docId, @page, @idx, @text)
                """;
            cmd.Parameters.AddWithValue("@docId", documentId);
            cmd.Parameters.AddWithValue("@page", chunk.PageNumber);
            cmd.Parameters.AddWithValue("@idx", chunk.ChunkIndex);
            cmd.Parameters.AddWithValue("@text", chunk.ChunkText);
            await cmd.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
    }

    public async Task<List<DocumentChunk>> SearchChunksAsync(string documentId, string query, int topK = 10)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        var sanitized = SanitizeFtsQuery(query);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT c.document_id, c.page_number, c.chunk_index, c.chunk_text
            FROM document_chunks c
            JOIN document_chunks_fts f ON f.rowid = c.id
            WHERE c.document_id = @docId
              AND document_chunks_fts MATCH @query
            ORDER BY bm25(document_chunks_fts)
            LIMIT @topK
            """;
        cmd.Parameters.AddWithValue("@docId", documentId);
        cmd.Parameters.AddWithValue("@query", sanitized);
        cmd.Parameters.AddWithValue("@topK", topK);

        var results = new List<DocumentChunk>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new DocumentChunk(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetString(3)));
        }

        return results;
    }

    public async Task<List<DocumentChunk>> GetAllChunksAsync(string documentId)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT document_id, page_number, chunk_index, chunk_text
            FROM document_chunks
            WHERE document_id = @docId
            ORDER BY chunk_index
            """;
        cmd.Parameters.AddWithValue("@docId", documentId);

        var results = new List<DocumentChunk>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new DocumentChunk(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetString(3)));
        }

        return results;
    }

    public async Task SaveEmbeddingsAsync(string documentId, List<(int ChunkIndex, float[] Embedding)> embeddings)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        foreach (var (chunkIndex, embedding) in embeddings)
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = (SqliteTransaction)tx;
            cmd.CommandText = """
                INSERT OR REPLACE INTO chunk_embeddings (document_id, chunk_index, embedding)
                VALUES (@docId, @idx, @emb)
                """;
            cmd.Parameters.AddWithValue("@docId", documentId);
            cmd.Parameters.AddWithValue("@idx", chunkIndex);
            cmd.Parameters.AddWithValue("@emb", FloatsToBytes(embedding));
            await cmd.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
    }

    /// <summary>
    /// Brute-force vector search: loads all embeddings for a document, computes cosine similarity,
    /// returns top-K chunk indices with scores. For single-document search on typical PDFs
    /// (&lt;1000 chunks), this completes in under 1ms.
    /// </summary>
    public async Task<List<(int ChunkIndex, double Score)>> VectorSearchAsync(
        string documentId, float[] queryEmbedding, int topK = 10)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT chunk_index, embedding FROM chunk_embeddings WHERE document_id = @docId";
        cmd.Parameters.AddWithValue("@docId", documentId);

        var results = new List<(int ChunkIndex, double Score)>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var chunkIndex = reader.GetInt32(0);
            var embeddingBytes = (byte[])reader[1];
            var embedding = BytesToFloats(embeddingBytes);
            var similarity = CosineSimilarity(queryEmbedding, embedding);
            results.Add((chunkIndex, similarity));
        }

        return results
            .OrderByDescending(r => r.Score)
            .Take(topK)
            .ToList();
    }

    /// <summary>Returns true if any embeddings exist for this document.</summary>
    public async Task<bool> HasEmbeddingsAsync(string documentId)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM chunk_embeddings WHERE document_id = @docId";
        cmd.Parameters.AddWithValue("@docId", documentId);

        var count = (long)(await cmd.ExecuteScalarAsync())!;
        return count > 0;
    }

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "is", "are", "was", "were", "be", "been", "being",
        "have", "has", "had", "do", "does", "did", "will", "would", "could",
        "should", "may", "might", "can", "shall", "to", "of", "in", "for",
        "on", "with", "at", "by", "from", "as", "into", "through", "about",
        "what", "which", "who", "whom", "this", "that", "these", "those",
        "it", "its", "and", "but", "or", "not", "no", "if", "how", "when",
        "where", "why",
    };

    internal static string SanitizeFtsQuery(string query)
    {
        var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Replace("\"", "").Trim())
            .Where(t => t.Length >= 2 && !StopWords.Contains(t))
            .ToList();

        if (tokens.Count == 0)
            return $"\"{query.Replace("\"", "")}\"";

        return string.Join(" OR ", tokens.Select(t => $"\"{t}\""));
    }

    private static byte[] FloatsToBytes(float[] floats)
    {
        var bytes = new byte[floats.Length * sizeof(float)];
        Buffer.BlockCopy(floats, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static float[] BytesToFloats(byte[] bytes)
    {
        var floats = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, floats, 0, bytes.Length);
        return floats;
    }

    private static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length) return 0;

        double dot = 0, normA = 0, normB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * (double)b[i];
            normA += a[i] * (double)a[i];
            normB += b[i] * (double)b[i];
        }

        var denom = Math.Sqrt(normA) * Math.Sqrt(normB);
        return denom == 0 ? 0 : dot / denom;
    }
}
