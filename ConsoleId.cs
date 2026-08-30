namespace Quark;

public static class ConsoleId
{
    private static readonly string[] Adjectives =
    [
        "Red", "Blue", "Gold", "Swift", "Brave", "Calm", "Dark", "Jade",
        "Iron", "Keen", "Lime", "Neon", "Opal", "Pink", "Rosy", "Sage",
        "Teal", "Volt", "Warm", "Zest", "Bold", "Cool", "Dusk", "Epic",
        "Fast", "Glow", "Hazy", "Icy", "Just", "Kiwi"
    ];

    private static readonly string[] Nouns =
    [
        "Switch", "Nova", "Pixel", "Spark", "Drift", "Flame", "Ghost",
        "Haven", "Ivory", "Jewel", "Karma", "Lunar", "Mango", "Nexus",
        "Orbit", "Prism", "Quest", "Radar", "Solar", "Titan", "Unity",
        "Vapor", "Wave", "Xenon", "Yield", "Zenith", "Apex", "Blaze",
        "Comet", "Delta"
    ];

    private static readonly Random _rng = new();

    public static string Generate()
    {
        string adj  = Adjectives[_rng.Next(Adjectives.Length)];
        string noun = Nouns[_rng.Next(Nouns.Length)];
        return $"{adj}-{noun}";
    }
}
