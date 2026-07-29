using FluentAssertions;
using Larpx.PersonalTools.FindMyFavouriteMusic.Sources.Abstractions;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Tests.Sources;

public class ResolvedAudioTests
{
    [Fact]
    public async Task DisposeAsync_DeletesTemporaryFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"resolved_{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(path, "x");
        File.Exists(path).Should().BeTrue();

        var audio = new ResolvedAudio { FilePath = path, IsTemporary = true };
        await audio.DisposeAsync();

        File.Exists(path).Should().BeFalse();
    }

    [Fact]
    public async Task DisposeAsync_RunsCleanup_ThenDeletesTemporaryFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"resolved_{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(path, "x");
        var cleanupCalled = false;

        var audio = new ResolvedAudio { FilePath = path, IsTemporary = true }
            .WithCleanup(() =>
            {
                cleanupCalled = true;
                return ValueTask.CompletedTask;
            });

        await audio.DisposeAsync();

        cleanupCalled.Should().BeTrue();
        File.Exists(path).Should().BeFalse();
    }

    [Fact]
    public async Task DisposeAsync_DoesNotDeleteNonTemporaryFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"resolved_{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(path, "keep");
        try
        {
            var audio = new ResolvedAudio { FilePath = path, IsTemporary = false };
            await audio.DisposeAsync();
            File.Exists(path).Should().BeTrue();
        }
        finally
        {
            File.Delete(path);
        }
    }
}
