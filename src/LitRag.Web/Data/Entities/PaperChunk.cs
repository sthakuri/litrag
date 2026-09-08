namespace LitRag.Web.Data.Entities;

public class PaperChunk
{
    public int Id { get; set; }

    public int PaperId { get; set; }
    public Paper? Paper { get; set; }

    public int ChunkIndex { get; set; }
    public int PageNumber { get; set; }
    public required string Text { get; set; }

    /// <summary>Embedding vector, packed as little-endian float32 bytes.</summary>
    public required byte[] Embedding { get; set; }

    public static byte[] PackEmbedding(float[] vector)
    {
        var bytes = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    public float[] UnpackEmbedding()
    {
        var vector = new float[Embedding.Length / sizeof(float)];
        Buffer.BlockCopy(Embedding, 0, vector, 0, Embedding.Length);
        return vector;
    }
}
