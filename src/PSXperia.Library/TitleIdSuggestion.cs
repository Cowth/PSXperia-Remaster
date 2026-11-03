namespace PSXperia.Library;

public static class TitleIdSuggestion
{
    public static string ForGame(GameEntry game)
    {
        ArgumentNullException.ThrowIfNull(game);

        var serial = string.IsNullOrWhiteSpace(game.DiscSerial) ? game.TitleId : game.DiscSerial;
        if (string.IsNullOrWhiteSpace(serial)) return "";

        var clean = new string(serial.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        return clean.Length > 0 ? clean + "_1" : "";
    }
}
