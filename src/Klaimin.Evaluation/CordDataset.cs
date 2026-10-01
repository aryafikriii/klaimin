using System.Globalization;
using System.Net;
using System.Text.Json;

namespace Klaimin.Evaluation;

public sealed record EvaluationReceipt(int Id, byte[] Image, EvaluationLabel Label);

public static class CordDataset
{
    private const string RowsUrl = "https://datasets-server.huggingface.co/rows?dataset=naver-clova-ix%2Fcord-v1&config=default&split=test&offset=0&length=100";
    private const string Attribution = "CORD: A Consolidated Receipt Dataset for Post-OCR Parsing, Seunghyun Park et al., 2019. Licensed under CC BY 4.0. Source: https://github.com/clovaai/cord";

    public static async Task DownloadAsync(string directory, CancellationToken cancellation = default)
    {
        using var client = new HttpClient();
        using var document = JsonDocument.Parse(await client.GetStreamAsync(RowsUrl, cancellation));
        var rows = document.RootElement.GetProperty("rows");
        if (rows.GetArrayLength() != 100) throw new InvalidDataException("CORD returned an incomplete test split.");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "ATTRIBUTION.txt"), Attribution, cancellation);

        foreach (var row in rows.EnumerateArray())
        {
            var id = row.GetProperty("row_idx").GetInt32();
            var data = row.GetProperty("row");
            var imagePath = ImagePath(directory, id);
            var labelPath = LabelPath(directory, id);
            if (!File.Exists(imagePath))
            {
                var imageUrl = new Uri(data.GetProperty("image").GetProperty("src").GetString()!);
                if (imageUrl.Scheme != Uri.UriSchemeHttps || imageUrl.Host != "datasets-server.huggingface.co")
                    throw new InvalidDataException("CORD returned an unexpected image host.");
                var image = await client.GetByteArrayAsync(imageUrl, cancellation);
                await File.WriteAllBytesAsync(imagePath, image, cancellation);
            }
            if (!File.Exists(labelPath))
                await File.WriteAllTextAsync(labelPath, data.GetProperty("ground_truth").GetString()!, cancellation);
        }
    }

    public static async Task<IReadOnlyList<EvaluationReceipt>> ReadAsync(
        string directory, int count, CancellationToken cancellation = default)
    {
        var receipts = new List<EvaluationReceipt>(count);
        for (var id = 0; id < count; id++)
        {
            var image = await File.ReadAllBytesAsync(ImagePath(directory, id), cancellation);
            var groundTruth = await File.ReadAllTextAsync(LabelPath(directory, id), cancellation);
            receipts.Add(new(id, image, ParseLabel(groundTruth)));
        }
        return receipts;
    }

    private static EvaluationLabel ParseLabel(string json)
    {
        using var document = JsonDocument.Parse(json);
        var parsed = document.RootElement.GetProperty("gt_parse");
        long? total = null;
        if (parsed.TryGetProperty("total", out var totalData) && totalData.TryGetProperty("total_price", out var totalElement))
            total = ReadAmount(totalElement);
        var names = new List<string?>();
        var prices = new List<long?>();
        ReadItems(parsed, "menu", names, prices);
        return new(total, names, prices);
    }

    private static void ReadItems(JsonElement parsed, string section, List<string?> names, List<long?> prices)
    {
        if (!parsed.TryGetProperty(section, out var items)) return;
        if (items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray()) ReadItem(item, names, prices);
        }
        else if (items.ValueKind == JsonValueKind.Object)
        {
            ReadItem(items, names, prices);
        }
    }

    private static void ReadItem(JsonElement item, List<string?> names, List<long?> prices)
    {
        if (item.TryGetProperty("nm", out var name)) names.Add(name.GetString());
        if (item.TryGetProperty("price", out var price)) prices.Add(ReadAmount(price));
        if (item.TryGetProperty("sub_nm", out var subName)) names.Add(subName.GetString());
        if (item.TryGetProperty("sub_price", out var subPrice)) prices.Add(ReadAmount(subPrice));
    }

    private static long? ReadAmount(JsonElement value)
    {
        var text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };
        if (text is null) return null;
        var digits = new string(text.Where(char.IsAsciiDigit).ToArray());
        return long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var amount) ? amount : null;
    }

    private static string ImagePath(string directory, int id) => Path.Combine(directory, $"receipt-{id:D3}.jpg");
    private static string LabelPath(string directory, int id) => Path.Combine(directory, $"receipt-{id:D3}.json");
}
