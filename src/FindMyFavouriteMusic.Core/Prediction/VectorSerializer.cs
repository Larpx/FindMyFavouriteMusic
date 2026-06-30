using System.Runtime.InteropServices;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Interfaces;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Core.Prediction;

/// <summary>
/// 向量序列化器：float[] 与 byte[] 双向零拷贝转换。
/// </summary>
/// <remarks>
/// 用途：SQLite 数据库的 BLOB 字段只能存储 byte[]，而内存中特征向量以 float[] 表示。
/// <para>实现原理：使用 <see cref="MemoryMarshal"/> 直接在内存层面重新解释数据，</para>
/// <para>无需逐元素复制，达到真正的零拷贝。</para>
/// <para>float 为 32 位（4 字节），故 byte 数组长度 = float 数量 × 4。</para>
/// <para>字节序：使用当前平台的本地字节序（little-endian on x86/x64），</para>
/// <para>由于序列化与反序列化在同一平台，无需考虑跨平台字节序问题。</para>
/// </remarks>
public class VectorSerializer : IVectorSerializer
{
    /// <inheritdoc/>
    /// <summary>
    /// 将 float 数组序列化为 byte 数组。
    /// </summary>
    /// <param name="vector">float 特征向量</param>
    /// <returns>byte 数组，长度 = vector.Length × 4</returns>
    public byte[] Serialize(float[] vector)
    {
        ArgumentNullException.ThrowIfNull(vector);

        var byteCount = vector.Length * sizeof(float);  // 每个 float 4 字节
        var bytes = new byte[byteCount];
        // AsBytes 将 float[] 视图重新解释为 byte[] 视图，CopyTo 完成实际拷贝
        MemoryMarshal.AsBytes(vector.AsSpan()).CopyTo(bytes);
        return bytes;
    }

    /// <inheritdoc/>
    /// <summary>
    /// 将 byte 数组反序列化为 float 数组。
    /// </summary>
    /// <param name="blob">BLOB 数据，长度必须为 4 的倍数</param>
    /// <returns>float 特征向量</returns>
    public float[] Deserialize(byte[] blob)
    {
        ArgumentNullException.ThrowIfNull(blob);

        var floatCount = blob.Length / sizeof(float);
        var floats = new float[floatCount];
        // Cast<byte, float> 将 byte span 重新解释为 float span，CopyTo 完成实际拷贝
        MemoryMarshal.Cast<byte, float>(blob.AsSpan()).CopyTo(floats);
        return floats;
    }
}
