using RapidFireKeys.Updates;
using Shouldly;
using Xunit;

namespace RapidFireKeys.Tests;

/// <summary>
/// Reading GitHub's "latest release" document. The asset to install is the exe itself, since
/// every release from 1.0.4 on ships RapidFireKeys as a single file.
/// </summary>
public class ReleaseFeedTests
{
    [Fact]
    public void AReleaseIsReadWithItsExe()
    {
        const string json = """
            {
              "tag_name": "v1.1.0",
              "name": "RapidFireKeys v1.1.0",
              "draft": false,
              "prerelease": false,
              "html_url": "https://github.com/levinium/RapidFireKeys/releases/tag/v1.1.0",
              "body": "- **Added a Settings window.**",
              "assets": [
                { "name": "RapidFireKeys.exe.sha256", "size": 90, "browser_download_url": "https://example/a.sha256" },
                {
                  "name": "RapidFireKeys.exe",
                  "size": 72050040,
                  "digest": "sha256:8a5b7df389b8cad15965238afd43a547465ab9553c41e89278ca290adad589d2",
                  "browser_download_url": "https://github.com/levinium/RapidFireKeys/releases/download/v1.1.0/RapidFireKeys.exe"
                }
              ]
            }
            """;

        var release = UpdateService.ParseRelease(json);

        release.ShouldNotBeNull();
        release.Tag.ShouldBe("v1.1.0");
        release.IsDraft.ShouldBeFalse();
        release.Asset.ShouldNotBeNull();
        release.Asset.Name.ShouldBe("RapidFireKeys.exe");
        release.Asset.Size.ShouldBe(72050040);
        release.Asset.Digest.ShouldStartWith("sha256:");
    }

    [Fact]
    public void AReleaseWithNoExeHasNoAsset()
    {
        const string json = """{ "tag_name": "v1.1.0", "assets": [ { "name": "notes.txt", "size": 1, "browser_download_url": "https://example/n" } ] }""";

        UpdateService.ParseRelease(json)!.Asset.ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<html>captive portal</html>")]
    [InlineData("[]")]
    [InlineData("""{ "assets": [] }""")]
    public void AnUnreadableAnswerIsNoRelease(string? json)
    {
        UpdateService.ParseRelease(json).ShouldBeNull();
    }

    [Fact]
    public async Task AFailedFetchIsUnknownNotAnException()
    {
        var service = new UpdateService(_ => throw new HttpRequestException("offline"));

        var verdict = await service.CheckAsync();

        verdict.Outcome.ShouldBe(UpdateOutcome.Unknown);
    }

    [Fact]
    public void ADownloadThatDoesNotMatchItsChecksumIsRefused()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "not the real exe");
            var size = new FileInfo(path).Length;

            var wrong = new ReleaseAsset("RapidFireKeys.exe", "https://example", size, "sha256:" + new string('0', 64));
            UpdateInstaller.Verify(path, wrong).ShouldNotBeNull();

            var truncated = new ReleaseAsset("RapidFireKeys.exe", "https://example", size + 1, null);
            UpdateInstaller.Verify(path, truncated).ShouldNotBeNull();

            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
            var right = new ReleaseAsset("RapidFireKeys.exe", "https://example", size, "sha256:" + hash.ToLowerInvariant());
            UpdateInstaller.Verify(path, right).ShouldBeNull();
        }
        finally
        {
            File.Delete(path);
        }
    }
}
