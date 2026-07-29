using FluentAssertions;
using Larpx.PersonalTools.FindMyFavouriteMusic.Services.Sources;
using Larpx.PersonalTools.FindMyFavouriteMusic.Sources.Abstractions;
using Moq;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Tests.Sources;

public class MusicSourceRegistryTests
{
    [Fact]
    public void TryGet_IsCaseInsensitive_And_GetRequired_ThrowsWhenMissing()
    {
        var local = new Mock<IMusicSourcePlugin>();
        local.SetupGet(p => p.Id).Returns(MusicSourceIds.Local);
        var netease = new Mock<IMusicSourcePlugin>();
        netease.SetupGet(p => p.Id).Returns(MusicSourceIds.Netease);

        var registry = new MusicSourceRegistry([local.Object, netease.Object]);

        registry.GetAll().Should().HaveCount(2);
        registry.TryGet("LOCAL").Should().BeSameAs(local.Object);
        registry.TryGet("NeTeAsE").Should().BeSameAs(netease.Object);
        registry.TryGet(MusicSourceIds.QQ).Should().BeNull();

        var act = () => registry.GetRequired("missing");
        act.Should().Throw<KeyNotFoundException>().WithMessage("*missing*");
    }
}
