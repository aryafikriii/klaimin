using Klaimin.Core;

namespace Klaimin.Evaluation;

public sealed record EvaluationLabel(
    long? Total,
    IReadOnlyList<string?> LineItemNames,
    IReadOnlyList<long?> LineItemPrices);

public sealed record EvaluationScore(bool? Total, bool? LineItemNames, bool? LineItemPrices);

public static class EvaluationScorer
{
    public static EvaluationScore Score(Extraction? extraction, EvaluationLabel label)
    {
        bool? total = label.Total is { } expectedTotal ? extraction?.Total == expectedTotal : null;
        var expectedNames = label.LineItemNames.Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name!.Trim()).ToArray();
        var expectedPrices = label.LineItemPrices.Where(price => price.HasValue).Select(price => price!.Value).ToArray();
        bool? names = expectedNames.Length == 0 ? null : extraction is not null &&
            extraction.LineItems.Select(item => item.Name.Trim()).SequenceEqual(expectedNames, StringComparer.OrdinalIgnoreCase);
        bool? prices = expectedPrices.Length == 0 ? null : extraction is not null &&
            extraction.LineItems.Select(item => item.Price).SequenceEqual(expectedPrices);

        return new(total, names, prices);
    }
}
