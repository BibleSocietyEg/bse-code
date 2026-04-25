using FluentAssertions;

namespace BSE_Code.Tests;

public class ThemeManagerTests
{
    [Theory]
    [InlineData("default")]
    [InlineData("dracula")]
    [InlineData("monokai")]
    [InlineData("ocean")]
    [InlineData("forest")]
    [InlineData("light")]
    public void TrySet_KnownTheme_ReturnsTrueAndSetsTheme(string themeName)
    {
        var result = ThemeManager.TrySet(themeName);

        result.Should().BeTrue();
        ThemeManager.Current.Name.Should().Be(themeName);
    }

    [Fact]
    public void TrySet_UnknownTheme_ReturnsFalse()
    {
        var result = ThemeManager.TrySet("nonexistent-theme");

        result.Should().BeFalse();
    }

    [Fact]
    public void TrySet_CaseInsensitive_Succeeds()
    {
        var result = ThemeManager.TrySet("DRACULA");

        result.Should().BeTrue();
    }

    [Fact]
    public void Names_ContainsAllBuiltInThemes()
    {
        ThemeManager.Names.Should().Contain(["default", "dracula", "monokai", "ocean", "forest", "light"]);
    }

    [Fact]
    public void BuiltIn_HasSixThemes()
    {
        ThemeManager.BuiltIn.Should().HaveCount(6);
    }

    [Fact]
    public void Current_DefaultsToDefaultTheme()
    {
        ThemeManager.TrySet("default");
        ThemeManager.Current.Name.Should().Be("default");
    }
}
