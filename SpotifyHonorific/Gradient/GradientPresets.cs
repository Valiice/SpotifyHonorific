namespace SpotifyHonorific.Gradient;

public static class GradientPresets
{
    private static readonly string[] Names =
    [
        "Pride Rainbow",
        "Transgender",
        "Lesbian",
        "Bisexual",
        "Black & White",
        "Black & Red",
        "Black & Blue",
        "Black & Yellow",
        "Black & Green",
        "Black & Pink",
        "Black & Cyan",
        "Cherry Blossom",
        "Golden",
        "Pastel Rainbow",
        "Dark Rainbow",
        "Non-binary",
    ];

    public static int NumPresets => Names.Length;

    public static string GetName(int index)
    {
        if (index < 0 || index >= Names.Length) return $"Unknown ({index})";
        return Names[index];
    }
}
