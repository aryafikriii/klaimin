namespace Klaimin.Core;

/// <summary>An accepted image on disk: its file name in the store and the type its content says it is.</summary>
public record StoredImage(string File, string ContentType);

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

    // ponytail: an upload that is never confirmed leaves its file behind. Sweep unreferenced files on a schedule if disk use matters.
    public async Task<(StoredImage? Image, Problem? Problem)> TryStoreAsync(Stream upload, long length)
    {
        if (length > MaxBytes)
            return (null, new("Image", "The image is larger than 5 MB. Choose a smaller photo of the receipt."));

        using var buffer = new MemoryStream();
        await upload.CopyToAsync(buffer);
        var bytes = buffer.ToArray();
        var contentType = ContentTypeOf(bytes);
        if (contentType is null)
            return (null, new("Image", "The file is not a JPEG, PNG, or WebP image. Choose a photo of the receipt."));

        Directory.CreateDirectory(root);
        var file = Guid.NewGuid().ToString("N");
        await File.WriteAllBytesAsync(Path.Combine(root, file), bytes);
        return (new(file, contentType), null);
    }

    public void Delete(string file) => File.Delete(Path.Combine(root, file));

    public string PathOf(string file) => Path.Combine(root, file);
}
