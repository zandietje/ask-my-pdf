namespace AskMyPdf.Infrastructure.Data;

using AskMyPdf.Core.Models;
using Microsoft.Data.Sqlite;

public class ChunkRepository(DbConnectionFactory db)
{
    public async Task SaveChunksAsync(string documentId, List<DocumentChunk> chunks)
    {
        await using var conn = await db.OpenConnectionAsync();
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
        await using var conn = await db.OpenConnectionAsync();

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
            results.Add(ReadChunk(reader));

        return results;
    }

    public async Task<List<DocumentChunk>> GetAllChunksAsync(string documentId)
    {
        await using var conn = await db.OpenConnectionAsync();

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
            results.Add(ReadChunk(reader));

        return results;
    }

    public async Task SaveEmbeddingsAsync(string documentId, List<(int ChunkIndex, float[] Embedding)> embeddings)
    {
        await using var conn = await db.OpenConnectionAsync();
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
        await using var conn = await db.OpenConnectionAsync();

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
        await using var conn = await db.OpenConnectionAsync();

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

    private static DocumentChunk ReadChunk(SqliteDataReader reader) =>
        new(reader.GetString(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetString(3));

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
