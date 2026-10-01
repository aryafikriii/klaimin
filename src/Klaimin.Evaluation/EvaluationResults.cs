namespace Klaimin.Evaluation;

public sealed record EvaluatedReceipt(
    int Id,
    EvaluationScore Score,
    double Seconds,
    long? InputTokens,
    long? OutputTokens,
    long? TotalTokens);

public sealed record FieldAccuracy(int Correct, int Scored, double? Percent);

public sealed record EvaluationSummary(FieldAccuracy Total, FieldAccuracy LineItemNames, FieldAccuracy LineItemPrices);

public sealed record EvaluationResults(
    string Model,
    string Dataset,
    string DatasetSource,
    string License,
    string Attribution,
    int ReceiptsRequested,
    int ReceiptsScored,
    EvaluationSummary Accuracy,
    IReadOnlyList<EvaluatedReceipt> Receipts)
{
    public static EvaluationResults Create(string model, int requested, IReadOnlyList<EvaluatedReceipt> receipts) => new(
        model,
        "CORD v1 test",
        "https://huggingface.co/datasets/naver-clova-ix/cord-v1",
        "CC BY 4.0",
        "Seunghyun Park et al., CORD: A Consolidated Receipt Dataset for Post-OCR Parsing (2019).",
        requested,
        receipts.Count,
        new(
            AccuracyFor(receipts.Select(receipt => receipt.Score.Total)),
            AccuracyFor(receipts.Select(receipt => receipt.Score.LineItemNames)),
            AccuracyFor(receipts.Select(receipt => receipt.Score.LineItemPrices))),
        receipts);

    private static FieldAccuracy AccuracyFor(IEnumerable<bool?> scores)
    {
        var scored = scores.Where(score => score.HasValue).Select(score => score!.Value).ToArray();
        var correct = scored.Count(score => score);
        return new(correct, scored.Length, scored.Length == 0 ? null : 100d * correct / scored.Length);
    }
}
