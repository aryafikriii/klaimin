using Klaimin.Core;
using Klaimin.Evaluation;

namespace Klaimin.Tests;

public class EvaluationScoringTests
{
    private static readonly EvaluationLabel Label = new(53_000, ["Nasi goreng", "Es teh"], [45_000, 8_000]);

    private static ExtractedLineItem Item(string name, long price) => new(name, price);

    private static Extraction Extracted(long? total, params ExtractedLineItem[] lineItems) => new(total, null, lineItems, null);

    [Fact]
    public void Scores_total_line_item_names_and_prices_against_labels()
    {
        var exact = EvaluationScorer.Score(Extracted(53_000, Item("Nasi goreng", 45_000), Item("Es teh", 8_000)), Label);
        var wrong = EvaluationScorer.Score(Extracted(50_000, Item("Nasi goreng", 44_000)), Label);

        Assert.Equal(new EvaluationScore(true, true, true), exact);
        Assert.Equal(new EvaluationScore(false, false, false), wrong);
    }

    [Fact]
    public void Does_not_score_fields_missing_from_the_label()
    {
        var score = EvaluationScorer.Score(null, new EvaluationLabel(null, [], []));

        Assert.Equal(new EvaluationScore(null, null, null), score);
    }

    [Fact]
    public void Names_match_whatever_their_letter_case_or_surrounding_spaces()
    {
        var score = EvaluationScorer.Score(Extracted(53_000, Item("  NASI GORENG ", 45_000), Item("es teh", 8_000)), Label);

        Assert.True(score.LineItemNames);
    }

    [Fact]
    public void Each_field_is_scored_on_its_own()
    {
        var namesOnly = EvaluationScorer.Score(Extracted(1, Item("Nasi goreng", 1), Item("Es teh", 2)), Label);
        var pricesOnly = EvaluationScorer.Score(Extracted(1, Item("Fried rice", 45_000), Item("Iced tea", 8_000)), Label);
        var totalOnly = EvaluationScorer.Score(Extracted(53_000), Label);

        Assert.Equal(new EvaluationScore(false, true, false), namesOnly);
        Assert.Equal(new EvaluationScore(false, false, true), pricesOnly);
        Assert.Equal(new EvaluationScore(true, false, false), totalOnly);
    }

    [Fact]
    public void Line_items_in_a_different_order_do_not_match()
    {
        var score = EvaluationScorer.Score(Extracted(53_000, Item("Es teh", 8_000), Item("Nasi goreng", 45_000)), Label);

        Assert.Equal(new EvaluationScore(true, false, false), score);
    }

    [Fact]
    public void One_missing_or_extra_line_item_fails_the_whole_list()
    {
        var missing = EvaluationScorer.Score(Extracted(53_000, Item("Nasi goreng", 45_000)), Label);
        var extra = EvaluationScorer.Score(
            Extracted(53_000, Item("Nasi goreng", 45_000), Item("Es teh", 8_000), Item("Kerupuk", 2_000)), Label);

        Assert.Equal(new EvaluationScore(true, false, false), missing);
        Assert.Equal(new EvaluationScore(true, false, false), extra);
    }

    [Fact]
    public void A_failed_extraction_is_wrong_on_every_labelled_field()
    {
        var score = EvaluationScorer.Score(null, Label);

        Assert.Equal(new EvaluationScore(false, false, false), score);
    }

    [Fact]
    public void A_label_with_only_some_fields_scores_only_those()
    {
        var totalOnly = new EvaluationLabel(53_000, [], []);
        var namesWithoutPrices = new EvaluationLabel(null, ["Nasi goreng", null, " "], [null]);

        var first = EvaluationScorer.Score(Extracted(53_000, Item("Nasi goreng", 45_000)), totalOnly);
        var second = EvaluationScorer.Score(Extracted(53_000, Item("Nasi goreng", 45_000)), namesWithoutPrices);

        Assert.Equal(new EvaluationScore(true, null, null), first);
        Assert.Equal(new EvaluationScore(null, true, null), second);
    }
}
