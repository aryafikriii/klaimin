namespace Klaimin.Core;

/// <summary>Receipt images on local disk, in a folder the web server does not serve.</summary>
public class ReceiptImages(string root)
{
    public const int MaxBytes = 5 * 1024 * 1024;

    /// <summary>Decides the type from the first bytes, never from the file name. Null when it is not an accepted image.</summary>
    public static string? ContentTypeOf(ReadOnlySpan<byte> bytes) => bytes switch
    {
        [0xFF, 0xD8, 0xFF, ..] => "image/jpeg",
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, ..] => "image/png",
        [0x52, 0x49, 0x46, 0x46, _, _, _, _, 0x57, 0x45, 0x42, 0x50, ..] => "image/webp",
        _ => null,
    };

    public async Task<string> SaveAsync(byte[] bytes)
    {
        Directory.CreateDirectory(root);
        var file = Guid.NewGuid().ToString("N");
        await File.WriteAllBytesAsync(Path.Combine(root, file), bytes);
        return file;
    }

    public string PathOf(string file) => Path.Combine(root, file);
}
