using System.Globalization;

namespace Klaimin.Core;

/// <summary>Whole Rupiah, written the Indonesian way: Rp 1.250.000.</summary>
public static class Rupiah
{
    private static readonly NumberFormatInfo Dots = new() { NumberGroupSeparator = "." };

    public static string Format(long amount) => "Rp " + amount.ToString("N0", Dots);

    /// <summary>Reads "125000", "125.000", or "Rp 125.000". Decimals and signs are refused.</summary>
    public static bool TryParse(string? text, out long amount)
    {
        var digits = (text ?? "").Replace("Rp", "", StringComparison.OrdinalIgnoreCase).Replace(".", "").Trim();
        amount = 0;
        return digits.Length is > 0 and <= 15
            && digits.All(char.IsAsciiDigit)
            && long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out amount);
    }
}
