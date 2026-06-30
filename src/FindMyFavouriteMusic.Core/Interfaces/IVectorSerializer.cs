namespace FindMyFavouriteMusic.Core.Interfaces;

/// <summary>
/// 向量序列化接口，float[] 与 byte[] 双向转换
/// </summary>
public interface IVectorSerializer
{
    /// <summary>将 float 数组序列化为 byte 数组</summary>
    byte[] Serialize(float[] vector);

    /// <summary>将 byte 数组反序列化为 float 数组</summary>
    float[] Deserialize(byte[] blob);
}
