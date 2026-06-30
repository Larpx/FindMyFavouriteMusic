using System.Runtime.InteropServices;
using FindMyFavouriteMusic.Core.Interfaces;

namespace FindMyFavouriteMusic.Core.Prediction;

/// <summary>
/// 向量序列化器，float[] 与 byte[] 双向转换
/// </summary>
public class VectorSerializer : IVectorSerializer
{
    /// <inheritdoc/>
    public byte[] Serialize(float[] vector)
    {
        ArgumentNullException.ThrowIfNull(vector);

        var byteCount = vector.Length * sizeof(float);
        var bytes = new byte[byteCount];
        MemoryMarshal.AsBytes(vector.AsSpan()).CopyTo(bytes);
        return bytes;
    }

    /// <inheritdoc/>
    public float[] Deserialize(byte[] blob)
    {
        ArgumentNullException.ThrowIfNull(blob);

        var floatCount = blob.Length / sizeof(float);
        var floats = new float[floatCount];
        MemoryMarshal.Cast<byte, float>(blob.AsSpan()).CopyTo(floats);
        return floats;
    }
}
