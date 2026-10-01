using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Klaimin.Core;
using Klaimin.Evaluation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

if (args.Length == 2 && args[0] == "download-cord")
{
    try
    {
        await CordDataset.DownloadAsync(args[1]);
        Console.WriteLine("Downloaded CORD v1 test split (100 receipts).");
        return 0;
    }
    catch (Exception error) when (error is HttpRequestException or IOException or UnauthorizedAccessException or JsonException)
    {
        Console.Error.WriteLine($"CORD download failed ({error.GetType().Name}).");
        return 1;
    }
}

if (args.Length is < 2 or > 3 || !int.TryParse(args[1], out var requested) || requested is < 1 or > 100 ||
    (args.Length == 3 && (!int.TryParse(args[2], out var delaySeconds) || delaySeconds is < 1 or > 3600)))
{
    Console.Error.WriteLine("Usage: Klaimin.Evaluation <model> <receipt-count> [delay-seconds] | download-cord <directory>");
    return 2;
}

var model = args[0];
if (model == "download-cord" || !Regex.IsMatch(model, "^[A-Za-z0-9._:-]+$"))
{
    Console.Error.WriteLine("Model name contains unsupported characters.");
    return 2;
}

var delay = TimeSpan.FromSeconds(args.Length == 3 ? int.Parse(args[2]) : 10);
var settings = new Dictionary<string, string?> { ["Extraction:Model"] = model };
if (Environment.GetEnvironmentVariable("Extraction__ApiKey") is { } apiKey) settings["Extraction:ApiKey"] = apiKey;
if (Environment.GetEnvironmentVariable("Extraction__Endpoint") is { } endpoint) settings["Extraction:Endpoint"] = endpoint;
var configuration = new ConfigurationBuilder()
    .AddUserSecrets(Assembly.GetExecutingAssembly(), optional: true)
    .AddInMemoryCollection(settings)
    .Build();
if (string.IsNullOrWhiteSpace(configuration["Extraction:ApiKey"]) || string.IsNullOrWhiteSpace(configuration["Extraction:Endpoint"]))
{
    Console.Error.WriteLine("Configure Extraction:ApiKey in user secrets or the environment, and Extraction:Endpoint in the environment.");
    return 2;
}

var dataPath = Environment.GetEnvironmentVariable("Evaluation__CordPath") ?? ".scratch/v1/cord-v1";
var resultsPath = Environment.GetEnvironmentVariable("Evaluation__ResultsPath") ?? "results";
Directory.CreateDirectory(resultsPath);
var resultFile = Path.Combine(resultsPath, $"{Uri.EscapeDataString(model)}.json");
var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
var prior = File.Exists(resultFile)
    ? JsonSerializer.Deserialize<EvaluationResults>(await File.ReadAllTextAsync(resultFile), jsonOptions)
    : null;
if (prior is not null && (prior.Model != model || prior.Receipts.Count > requested))
{
    Console.Error.WriteLine("Existing results do not match this model or receipt count.");
    return 2;
}

var receipts = await CordDataset.ReadAsync(dataPath, requested);
var services = new ServiceCollection().AddLogging();
services.AddReceiptExtraction(configuration);
using var provider = services.BuildServiceProvider();
var extractor = provider.GetRequiredService<IReceiptExtractor>();
var results = prior?.Receipts.ToList() ?? [];
using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; shutdown.Cancel(); };

try
{
    for (var index = results.Count; index < receipts.Count; index++)
    {
        await Task.Delay(delay, shutdown.Token);
        var receipt = receipts[index];
        var started = Stopwatch.GetTimestamp();
        var result = await extractor.ExtractAsync(receipt.Image, "image/jpeg", [], shutdown.Token);
        var elapsed = Stopwatch.GetElapsedTime(started).TotalSeconds;
        if (result.Outcome != ExtractionOutcome.Extracted || result.Extraction is null)
        {
            Console.Error.WriteLine($"Extraction failed for receipt {receipt.Id}; existing results were kept for resume.");
            return 1;
        }

        results.Add(new(
            receipt.Id,
            EvaluationScorer.Score(result.Extraction, receipt.Label),
            elapsed,
            result.InputTokens,
            result.OutputTokens,
            result.TotalTokens));
        var evaluation = EvaluationResults.Create(model, requested, results);
        var temporaryFile = resultFile + ".tmp";
        await using (var output = File.Create(temporaryFile))
            await JsonSerializer.SerializeAsync(output, evaluation, jsonOptions, shutdown.Token);
        File.Move(temporaryFile, resultFile, overwrite: true);
        Console.WriteLine($"Scored {results.Count}/{requested} receipts ({elapsed:F1}s).");
    }
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Stopped. Completed receipts were saved and can be resumed.");
    return 130;
}

return 0;
