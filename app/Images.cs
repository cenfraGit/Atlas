using SkiaSharp;

namespace Atlas;

/// <summary>images dropped onto a board. the bytes live in .atlas/images so
/// they travel with the repo like boards and notes do. everything is
/// re-encoded on the way in: a raw clipboard screenshot is a few megabytes of
/// binary that would sit in git forever.</summary>
public static class ImageStore
{
    const int MaxSide = 2000;
    const long TryJpegAbove = 400 * 1024;

    public static string DirFor(string root) => Path.Combine(root, ".atlas", "images");

    static readonly Dictionary<string, SKImage?> Cache = [];

    public static SKImage? Load(string root, string name)
    {
        if (Cache.TryGetValue(name, out var hit)) return hit;
        SKImage? img = null;
        try
        {
            var path = Path.Combine(DirFor(root), name);
            if (File.Exists(path)) img = SKImage.FromEncodedData(path);
        }
        catch { }
        Cache[name] = img;
        return img;
    }

    /// <summary>write a bitmap in and return the file name to reference it by,
    /// or null if it could not be written.</summary>
    public static string? Save(string root, SKBitmap bitmap)
    {
        try
        {
            using var fitted = Fit(bitmap);
            var (bytes, ext) = Encode(fitted);
            var name = BookmarkStore.NewId() + "." + ext;
            Directory.CreateDirectory(DirFor(root));
            File.WriteAllBytes(Path.Combine(DirFor(root), name), bytes);
            return name;
        }
        catch (Exception ex)
        {
            Console.WriteLine("could not save image: " + ex.Message);
            return null;
        }
    }

    public static string? Import(string root, string path)
    {
        try
        {
            using var bitmap = SKBitmap.Decode(path);
            return bitmap is null ? null : Save(root, bitmap);
        }
        catch { return null; }
    }

    /// <summary>delete image files no board points at any more. safe only once
    /// the undo history that could bring an item back has been dropped.</summary>
    public static int Prune(string root, IEnumerable<Board> boards)
    {
        var dir = DirFor(root);
        if (!Directory.Exists(dir)) return 0;

        var live = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in boards)
            foreach (var it in b.Items)
                if (it.Kind == "image" && it.File is { } f) live.Add(f);

        int gone = 0;
        foreach (var path in Directory.EnumerateFiles(dir))
        {
            var name = Path.GetFileName(path);
            if (live.Contains(name)) continue;
            try { File.Delete(path); Cache.Remove(name); gone++; } catch { }
        }
        return gone;
    }

    static SKBitmap Fit(SKBitmap src)
    {
        int side = Math.Max(src.Width, src.Height);
        if (side <= MaxSide) return src.Copy();
        float s = (float)MaxSide / side;
        var info = new SKImageInfo(Math.Max(1, (int)(src.Width * s)), Math.Max(1, (int)(src.Height * s)));
        return src.Resize(info, SKFilterQuality.High) ?? src.Copy();
    }

    static (byte[] Bytes, string Ext) Encode(SKBitmap bitmap)
    {
        using var img = SKImage.FromBitmap(bitmap);
        using var png = img.Encode(SKEncodedImageFormat.Png, 100);
        var asPng = png.ToArray();

        // a screenshot is opaque and compresses far better as jpeg. anything
        // with transparency has to stay png or it grows a black background
        if (asPng.LongLength <= TryJpegAbove || HasAlpha(bitmap)) return (asPng, "png");

        using var jpeg = img.Encode(SKEncodedImageFormat.Jpeg, 85);
        var asJpeg = jpeg.ToArray();
        return asJpeg.LongLength < asPng.LongLength ? (asJpeg, "jpg") : (asPng, "png");
    }

    static bool HasAlpha(SKBitmap bitmap)
    {
        if (bitmap.AlphaType == SKAlphaType.Opaque) return false;
        // ponytail: samples a grid rather than every pixel. a stray
        // transparent pixel between samples costs a slightly larger file
        for (int y = 0; y < bitmap.Height; y += 8)
            for (int x = 0; x < bitmap.Width; x += 8)
                if (bitmap.GetPixel(x, y).Alpha < 250) return true;
        return false;
    }

    /// <summary>windows hands a screenshot over as a DIB: the same bytes as a
    /// .bmp file without its 14 byte header. put the header back and skia
    /// decodes it.</summary>
    public static SKBitmap? FromDib(byte[] dib)
    {
        if (dib.Length < 40) return null;
        int headerSize = BitConverter.ToInt32(dib, 0);
        int bits = BitConverter.ToInt16(dib, 14);
        int compression = BitConverter.ToInt32(dib, 16);
        int used = BitConverter.ToInt32(dib, 32);

        int palette = bits <= 8 ? (used == 0 ? 1 << bits : used) * 4 : 0;
        int masks = compression == 3 && headerSize == 40 ? 12 : 0;
        int offset = 14 + headerSize + masks + palette;
        if (offset > 14 + dib.Length) return null;

        var file = new byte[14 + dib.Length];
        file[0] = (byte)'B';
        file[1] = (byte)'M';
        BitConverter.GetBytes(file.Length).CopyTo(file, 2);
        BitConverter.GetBytes(offset).CopyTo(file, 10);
        dib.CopyTo(file, 14);
        try { return SKBitmap.Decode(file); } catch { return null; }
    }
}

/// <summary>reading an image off the windows clipboard. avalonia's clipboard
/// only hands back formats it knows, and a screenshot is not one of them: what
/// is actually there is CF_DIB, or a registered "PNG" format, and both have to
/// be asked for by hand.</summary>
public static class ClipboardImage
{
    const uint CfDib = 8, CfDibV5 = 17;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool OpenClipboard(nint owner);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool CloseClipboard();
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern nint GetClipboardData(uint format);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool IsClipboardFormatAvailable(uint format);
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern uint RegisterClipboardFormat(string name);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    static extern nint GlobalLock(nint handle);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    static extern bool GlobalUnlock(nint handle);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    static extern nuint GlobalSize(nint handle);

    public static SKBitmap? Read()
    {
        if (!OperatingSystem.IsWindows()) return null;

        // another app can hold the clipboard open for a moment after a copy
        bool open = false;
        for (int tries = 0; tries < 6 && !open; tries++)
        {
            open = OpenClipboard(0);
            if (!open) Thread.Sleep(50);
        }
        if (!open) return null;

        try
        {
            uint png = RegisterClipboardFormat("PNG");
            if (png != 0 && IsClipboardFormatAvailable(png) && Bytes(png) is { } encoded)
            {
                var fromPng = SKBitmap.Decode(encoded);
                if (fromPng is not null) return fromPng;
            }

            // windows synthesises CF_DIB from a plain CF_BITMAP, so this also
            // covers apps that only put a bitmap on the clipboard
            foreach (var format in new[] { CfDibV5, CfDib })
            {
                if (!IsClipboardFormatAvailable(format) || Bytes(format) is not { } dib) continue;
                var fromDib = ImageStore.FromDib(dib);
                if (fromDib is not null) return fromDib;
            }
            return null;
        }
        catch { return null; }
        finally { CloseClipboard(); }
    }

    static byte[]? Bytes(uint format)
    {
        var handle = GetClipboardData(format);
        if (handle == 0) return null;
        var ptr = GlobalLock(handle);
        if (ptr == 0) return null;
        try
        {
            int size = (int)GlobalSize(handle);
            if (size <= 0) return null;
            var buffer = new byte[size];
            System.Runtime.InteropServices.Marshal.Copy(ptr, buffer, 0, size);
            return buffer;
        }
        finally { GlobalUnlock(handle); }
    }
}
