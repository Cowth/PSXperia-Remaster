using PSXperia.Library;

namespace PSXperia.Engine.Tests;

public sealed class TitleIdSuggestionTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void ForGame_UsesDiscSerialAndPreservesSuggestedFormat()
    {
        var game = CreateGame(titleId: "OTHER", discSerial: "slus-123.45");

        Assert.Equal("SLUS12345_1", TitleIdSuggestion.ForGame(game));
    }

    [Theory]
    [InlineData("D (USA)", "SLUS-001.28", "SLUS00128_1")]
    [InlineData("Spyro 2 - Ripto's Rage! (USA)", "SCUS-944.25", "SCUS94425_1")]
    [InlineData("Crash Bash (USA)", "SCUS-945.70", "SCUS94570_1")]
    [Trait("Category", "Unit")]
    public void ForGame_UsesDisplayedLibrarySerials(string title, string discSerial, string expected)
    {
        var game = CreateGame(titleId: discSerial, discSerial: discSerial) with { Title = title };

        Assert.Equal(expected, TitleIdSuggestion.ForGame(game));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ForGame_FallsBackToTitleIdWhenDiscSerialIsBlank()
    {
        var game = CreateGame(titleId: "scus-543.21", discSerial: "  ");

        Assert.Equal("SCUS54321_1", TitleIdSuggestion.ForGame(game));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("   ", "")]
    [InlineData(null, "---")]
    [Trait("Category", "Unit")]
    public void ForGame_ReturnsEmptyWhenNeitherValueContainsAUsableSerial(string? titleId, string? discSerial)
    {
        var game = CreateGame(titleId, discSerial);

        Assert.Equal("", TitleIdSuggestion.ForGame(game));
    }

    private static GameEntry CreateGame(string? titleId, string? discSerial) =>
        new(Guid.NewGuid(), "Synthetic game", titleId, "synthetic.cue", null, CompatibilityStatus.Untested, DateTime.UtcNow)
        {
            DiscSerial = discSerial
        };
}
