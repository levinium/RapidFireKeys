using RapidFireKeys.Updates;
using Shouldly;
using Xunit;

namespace RapidFireKeys.Tests;

/// <summary>
/// Reading a version out of a release tag, and ordering two of them.
///
/// <para>
/// Getting this wrong fails silently in both directions: too eager and every user is told forever
/// that a version they already have is waiting for them; too shy and nobody is ever told about
/// anything again. Neither produces a bug report, because in one case the update is already
/// installed and in the other there is nothing to see.
/// </para>
/// </summary>
public class ReleaseVersionTests
{
    /// <summary>
    /// This repository's release names carry the product name ("RapidFireKeys v1.0.5"), and the
    /// feed falls back to the name when there is no tag. The digit trap from the sibling D2RExtractor
    /// project is kept too: there the first digit in "D2RExtractor_v1.1.7" is the 2 in "D2R", and a
    /// parser that scans forward to the first digit announces a phantom 2.0.0 to everyone.
    /// </summary>
    [Theory]
    [InlineData("RapidFireKeys v1.0.5", 1, 0, 5)]
    [InlineData("RapidFireKeys_v1.1.0", 1, 1, 0)]
    [InlineData("D2RExtractor_v1.1.7", 1, 1, 7)]
    [InlineData("D2RExtractor v1.1.7", 1, 1, 7)]
    [InlineData("D2RExtractor_v2.0", 2, 0, 0)]
    public void TheProductNameInATagIsNotPartOfTheVersion(string tag, int major, int minor, int patch)
    {
        ReleaseVersion.TryParse(tag, out var v).ShouldBeTrue();

        v.Major.ShouldBe(major);
        v.Minor.ShouldBe(minor);
        v.Patch.ShouldBe(patch);
    }

    [Theory]
    [InlineData("v1.1.8", 1, 1, 8)]
    [InlineData("V2.10.3", 2, 10, 3)]
    [InlineData("1.2.0", 1, 2, 0)]
    [InlineData("  1.2.3  ", 1, 2, 3)]
    [InlineData("1.2", 1, 2, 0)]
    [InlineData("1", 1, 0, 0)]
    [InlineData("1.0.0+abc123", 1, 0, 0)]
    public void APlainVersionIsReadAsWritten(string text, int major, int minor, int patch)
    {
        ReleaseVersion.TryParse(text, out var v).ShouldBeTrue();

        v.Major.ShouldBe(major);
        v.Minor.ShouldBe(minor);
        v.Patch.ShouldBe(patch);
        v.IsPreRelease.ShouldBeFalse();
    }

    /// <summary>
    /// A tag or name with no version in it at all ("RapidFireKeys") has to fail rather than be guessed at — an unreadable tag is reported as unknown, and
    /// unknown never offers anyone an update.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("RapidFireKeys")]
    [InlineData("not-a-version")]
    [InlineData("v")]
    [InlineData("1.2.3.4")]
    [InlineData("1.x.3")]
    [InlineData("1.0.0-")]
    public void ATagWithNoReadableVersionIsRefused(string? tag)
    {
        ReleaseVersion.TryParse(tag, out _).ShouldBeFalse();
    }

    /// <summary>
    /// The comparison that has to be numeric. As text, "1.10.0" sorts below "1.9.0", which would
    /// tell everyone on 1.9.0 they are current and then never mention a release again.
    /// </summary>
    [Fact]
    public void VersionsCompareAsNumbersNotAsText()
    {
        ReleaseVersion.TryParse("1.10.0", out var ten).ShouldBeTrue();
        ReleaseVersion.TryParse("1.9.0", out var nine).ShouldBeTrue();

        ten.IsNewerThan(nine).ShouldBeTrue();
        nine.IsNewerThan(ten).ShouldBeFalse();
    }

    /// <summary>
    /// A pre-release precedes the finished version of the same number. Backwards, this offers
    /// someone on 1.1.7 a "newer" 1.1.7-rc1, which is a downgrade.
    /// </summary>
    [Fact]
    public void AReleaseCandidateIsOlderThanTheReleaseItPrecedes()
    {
        ReleaseVersion.TryParse("1.1.7", out var released).ShouldBeTrue();
        ReleaseVersion.TryParse("1.1.7-rc1", out var candidate).ShouldBeTrue();

        released.IsNewerThan(candidate).ShouldBeTrue();
        candidate.IsNewerThan(released).ShouldBeFalse();
        candidate.IsPreRelease.ShouldBeTrue();
    }

    [Fact]
    public void PreReleaseIdentifiersCompareNumericallyWhereTheyAreNumbers()
    {
        ReleaseVersion.TryParse("1.0.0-rc.10", out var ten).ShouldBeTrue();
        ReleaseVersion.TryParse("1.0.0-rc.2", out var two).ShouldBeTrue();

        ten.IsNewerThan(two).ShouldBeTrue();
    }

    [Fact]
    public void TheSameVersionIsNotNewerThanItself()
    {
        ReleaseVersion.TryParse("1.1.8", out var a).ShouldBeTrue();
        ReleaseVersion.TryParse("D2RExtractor_v1.1.8", out var b).ShouldBeTrue();

        a.IsNewerThan(b).ShouldBeFalse();
        b.IsNewerThan(a).ShouldBeFalse();
        a.ShouldBe(b);
    }
}
