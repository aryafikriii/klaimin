using System.ClientModel;
using System.Globalization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenAI;

namespace Klaimin.Core;

public record ExtractedLineItem(string Name, long Price);

/// <summary>What a model proposes for one receipt. Any field it could not read is null.</summary>
public record Extraction(long? Total, DateOnly? Date, IReadOnlyList<ExtractedLineItem> LineItems, string? Category);

public enum ExtractionOutcome
{
    Extracted,
    Failed,
    Unavailable,
}

public record ExtractionResult(
    ExtractionOutcome Outcome,
    Extraction? Extraction = null,
    long? InputTokens = null,
    long? OutputTokens = null,
    long? TotalTokens = null)
{
    public static readonly ExtractionResult Failed = new(ExtractionOutcome.Failed);
    public static readonly ExtractionResult Unavailable = new(ExtractionOutcome.Unavailable);
}

public interface IReceiptExtractor
{
    /// <summary>Never throws for a provider problem: an error, a quota limit, or an unreadable answer is a failed result.</summary>
    Task<ExtractionResult> ExtractAsync(
        byte[] image, string contentType, IReadOnlyList<string> categories, CancellationToken cancellation = default);
}

/// <summary>Stands in when no API key is configured, so the app runs on manual entry alone.</summary>
public class NoReceiptExtractor : IReceiptExtractor
{
    public Task<ExtractionResult> ExtractAsync(
        byte[] image, string contentType, IReadOnlyList<string> categories, CancellationToken cancellation = default) =>
        Task.FromResult(ExtractionResult.Unavailable);
}

public class ModelReceiptExtractor(IChatClient chat, ILogger<ModelReceiptExtractor> log) : IReceiptExtractor
{
    internal sealed record Answer(long? Total, string? Date, List<AnswerLineItem>? LineItems, string? Category);

    internal sealed record AnswerLineItem(string? Name, long? Price);

    public async Task<ExtractionResult> ExtractAsync(
        byte[] image, string contentType, IReadOnlyList<string> categories, CancellationToken cancellation = default)
    {
        var prompt = $"""
            Read this photo of an Indonesian receipt.
            Amounts are whole Rupiah. A dot is a thousands separator, so 45.000 means 45000.
            Return the total the customer paid, the date printed on the receipt as YYYY-MM-DD,
            and every purchased line item with its price.
            For the category, pick the one that fits best from this list: {string.Join(", ", categories)}.
            Use null for anything you cannot read. Do not guess.
            """;
        try
        {
            var message = new ChatMessage(ChatRole.User, [new TextContent(prompt), new DataContent(image, contentType)]);
            var response = await chat.GetResponseAsync<Answer>([message], cancellationToken: cancellation);
            if (!response.TryGetResult(out var answer) || answer is null)
            {
                log.LogWarning("The model's answer was not the expected receipt structure.");
                return ExtractionResult.Failed;
            }

            var lineItems = (answer.LineItems ?? [])
                .Where(item => !string.IsNullOrWhiteSpace(item.Name) && item.Price is >= 0)
                .Select(item => new ExtractedLineItem(item.Name!.Trim(), item.Price!.Value))
                .ToList();
            var date = DateOnly.TryParseExact(answer.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                ? parsed
                : (DateOnly?)null;
            return new(
                ExtractionOutcome.Extracted,
                new Extraction(answer.Total is > 0 ? answer.Total : null, date, lineItems, answer.Category),
                response.Usage?.InputTokenCount,
                response.Usage?.OutputTokenCount,
                response.Usage?.TotalTokenCount);
        }
        // Whatever the provider throws (network, quota, bad key, malformed JSON), the claimant falls back to manual entry.
        catch (Exception error) when (!cancellation.IsCancellationRequested)
        {
            log.LogWarning(error, "Receipt extraction failed.");
            return ExtractionResult.Failed;
        }
    }
}

public static class ExtractionSetup
{
    /// <summary>
    /// Reads Extraction:Endpoint, Extraction:Model, and Extraction:ApiKey. The endpoint speaks the OpenAI chat
    /// protocol, which Gemini and most other providers offer, so changing provider is a configuration change.
    /// </summary>
    public static IServiceCollection AddReceiptExtraction(this IServiceCollection services, IConfiguration configuration)
    {
        var key = configuration["Extraction:ApiKey"];
        if (string.IsNullOrWhiteSpace(key)) return services.AddSingleton<IReceiptExtractor, NoReceiptExtractor>();

        var endpoint = configuration["Extraction:Endpoint"] ?? throw new InvalidOperationException("Extraction:Endpoint is not configured.");
        var model = configuration["Extraction:Model"] ?? throw new InvalidOperationException("Extraction:Model is not configured.");
        var client = new OpenAIClient(
            new ApiKeyCredential(key),
            new OpenAIClientOptions { Endpoint = new Uri(endpoint), NetworkTimeout = TimeSpan.FromSeconds(30) });
        services.AddSingleton(client.GetChatClient(model).AsIChatClient());
        return services.AddSingleton<IReceiptExtractor, ModelReceiptExtractor>();
    }
}
