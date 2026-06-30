using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Prediction;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Tests.Core;

public class VectorSerializerTests
{
    private readonly VectorSerializer _serializer = new();

    [Fact]
    public void Serialize_Deserialize_RoundTrip()
    {
        var original = new float[] { 1.5f, -2.3f, 0f, 100.001f };
        var blob = _serializer.Serialize(original);
        var restored = _serializer.Deserialize(blob);

        Assert.Equal(original.Length, restored.Length);
        for (var i = 0; i < original.Length; i++)
        {
            Assert.Equal(original[i], restored[i], 0.0001f);
        }
    }

    [Fact]
    public void Serialize_EmptyArray_ReturnsEmptyBlob()
    {
        var original = Array.Empty<float>();
        var blob = _serializer.Serialize(original);

        Assert.Empty(blob);
    }

    [Fact]
    public void Serialize_NullVector_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _serializer.Serialize(null!));
    }

    [Fact]
    public void Deserialize_NullBlob_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _serializer.Deserialize(null!));
    }
}
