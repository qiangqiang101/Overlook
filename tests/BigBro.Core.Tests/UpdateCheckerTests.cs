using BigBro.Core.Updates;

namespace BigBro.Core.Tests;

public class UpdateCheckerTests
{
    private static string Release(string tag, bool prerelease = false) =>
        $$"""{"tag_name":"{{tag}}","html_url":"https://github.com/x/y/releases/tag/{{tag}}","body":"notes","draft":false,"prerelease":{{(prerelease ? "true" : "false")}}}""";

    [Theory]
    [InlineData("v2.1.0", 2, 1, 0)]
    [InlineData("2.0.5-dev.3", 2, 0, 5)]
    [InlineData("3.0", 3, 0, 0)]
    public void Tags_are_read_as_versions(string tag, int major, int minor, int build)
    {
        Assert.True(UpdateChecker.TryParseVersion(tag, out var v));
        Assert.Equal(new Version(major, minor, build), v);
    }

    [Fact]
    public void Static_setup_does_not_throw() => Assert.NotNull(UpdateChecker.CurrentVersion);

    [Fact]
    public void Newer_release_is_offered()
    {
        var result = UpdateChecker.Parse(Release("v2.1.0"), new Version(2, 0, 0));
        Assert.Null(result.Error);
        Assert.Equal(new Version(2, 1, 0), result.Update!.Version);
        Assert.Equal("https://github.com/x/y/releases/tag/v2.1.0", result.Update.Url);
    }

    [Theory]
    [InlineData("v2.0.0")]
    [InlineData("v1.9.9")]
    public void Same_or_older_release_is_not_an_update(string tag) =>
        Assert.Null(UpdateChecker.Parse(Release(tag), new Version(2, 0, 0)).Update);

    [Fact]
    public void Prerelease_is_ignored() =>
        Assert.Null(UpdateChecker.Parse(Release("v9.0.0", prerelease: true), new Version(2, 0, 0)).Update);

    [Fact]
    public void Unreadable_answers_are_errors_not_crashes()
    {
        Assert.True(UpdateChecker.Parse("<html>", new Version(2, 0, 0)).Failed);
        Assert.True(UpdateChecker.Parse(Release("nightly"), new Version(2, 0, 0)).Failed);
    }
}
