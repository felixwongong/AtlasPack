using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using RectpackSharp;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

public struct Rect
{
    public int x { get; }
    public int y { get; }
    public int width { get; }
    public int height { get; }

    [JsonConstructor]
    public Rect(int x, int y, int width, int height)
    {
        this.x = x;
        this.y = y;
        this.width = width;
        this.height = height;
    }
}

public struct Size
{
    public int width { get; }
    public int height { get; }
    
    [JsonConstructor]
    public Size(int width, int height)
    {
        this.width = width;
        this.height = height;
    }
}

public class AtlasContext
{
    public Size bounds { get; set; }
    public IReadOnlyDictionary<string, Rect> imageRectMap { get; set; } = new Dictionary<string, Rect>();
}

public class AtlasPack
{
    public readonly AtlasContext context;
    public readonly AtlasImage image;

    public AtlasPack(AtlasContext context, AtlasImage image)
    {
        this.context = context;
        this.image = image;
    }
}

public struct AtlasImage
{
    public readonly byte[] imageData;

    public AtlasImage(byte[] imageData)
    {
        this.imageData = imageData;
    }
}

public static partial class AtlasPacker
{
    private static readonly string[] imageExtensions = new[]
        { "*.bmp", "*.gif", "*.jpg", "*.jpeg", "*.png", "*.tif", "*.tiff" };

    private static readonly string atlasFileName = "atlas.png";
    private static readonly string contextFileName = "context.json";
    
    /// <summary>Get All image as full path inside target directory recursively</summary>
    /// <param name="directory">directory full path</param>
    /// <returns>Full name of all images in the directory</returns>
    public static string[] GetImages(string directory)
    {
        var directoryInfo = new DirectoryInfo(directory);
        if (!directoryInfo.Exists)
        {
            return Array.Empty<string>();
        }

        var imagePaths = new List<string>();
        foreach (var extension in imageExtensions)
        {
            var files = directoryInfo.GetFiles(extension, SearchOption.AllDirectories);
            imagePaths.AddRange(files.Select(x => x.FullName));
        }

        return imagePaths.ToArray();
    }
    
    public static AtlasPack? PackAtlas(ReadOnlySpan<string> sourceImagePaths)
    {
        if (sourceImagePaths.Length <= 0)
            return null;

        var imageMap = LoadImages(sourceImagePaths);
        if (imageMap.Count == 0)
            return null;

        AtlasContext? atlasContext;
        try
        {
            GetRectPackResult(imageMap, out atlasContext);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }

        var atlasImage = PackImage(atlasContext, imageMap);
        return new AtlasPack(atlasContext, atlasImage);
    }

    public static void PackAtlas(ReadOnlySpan<string> sourceImagePath, string atlasPath)
    {
        AtlasPack? atlasPack;
        try
        {
            atlasPack = PackAtlas(sourceImagePath);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }

        if (atlasPack == null)
        {
            throw new Exception("Failed to pack atlas. No images found or packing failed.");
        }

        CreateFileDirectoryIfNotExist(atlasPath);

        using var archiveStream = new FileStream(atlasPath, FileMode.OpenOrCreate);
        SavePackAsAtlas(atlasPack, archiveStream);
    }

    public static void PackAtlasAsFolder(ReadOnlySpan<string> sourceImagePath, string folderPath)
    {
        AtlasPack? atlasPack;
        try
        {
            atlasPack = PackAtlas(sourceImagePath);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }

        if (atlasPack == null)
        {
            throw new Exception("Failed to pack atlas. No images found or packing failed.");
        }

        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }

        SavePackAsFolder(atlasPack, folderPath);
    }

    public static void SavePackAsAtlas(AtlasPack atlasPack, Stream stream)
    {
        if (atlasPack == null)
        {
            throw new ArgumentNullException(nameof(atlasPack));
        }

        if (stream == null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        var atlasImage = atlasPack.image;
        var context = atlasPack.context;

        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, false);

        var atlasEntry = archive.CreateEntry(atlasFileName);
        using (var entryStream = atlasEntry.Open())
        {
            entryStream.Write(atlasImage.imageData, 0, atlasImage.imageData.Length);
        }

        var contextEntry = archive.CreateEntry(contextFileName);
        using (var entryStream = contextEntry.Open())
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            JsonSerializer.Serialize(entryStream, context, options);
        }
    }

    public static AtlasPack LoadAtlas(string atlasPath)
    {
        if (string.IsNullOrEmpty(atlasPath))
        {
            throw new ArgumentNullException(nameof(atlasPath));
        }

        if (!File.Exists(atlasPath))
        {
            throw new FileNotFoundException("Atlas file not found.", atlasPath);
        }

        using var archive = ZipFile.OpenRead(atlasPath);
        var atlasEntry = archive.GetEntry(atlasFileName);
        if (atlasEntry == null)
        {
            throw new FileNotFoundException("Atlas image not found in the archive.");
        }

        using var atlasStream = atlasEntry.Open();
        var atlasImageData = new byte[atlasEntry.Length];
        var offset = 0;
        var remaining = atlasImageData.Length;

        while (remaining > 0)
        {
            int bytesRead = atlasStream.Read(atlasImageData, offset, remaining);
            if (bytesRead == 0)
            {
                throw new EndOfStreamException("Unexpected end of stream while reading the atlas image data.");
            }

            offset += bytesRead;
            remaining -= bytesRead;
        }

        var contextEntry = archive.GetEntry(contextFileName);
        if (contextEntry == null)
        {
            throw new FileNotFoundException("Context file not found in the archive.");
        }

        using var contextStream = contextEntry.Open();
        var atlasContext = JsonSerializer.Deserialize<AtlasContext>(contextStream);
        if (atlasContext == null)
        {
            throw new InvalidOperationException("Failed to deserialize context from the archive.");
        }
        var atlasImage = new AtlasImage(atlasImageData);

        return new AtlasPack(atlasContext, atlasImage);
    }

    public static void SavePackAsFolder(AtlasPack atlasPack, string atlasFolderPath)
    {
        if (atlasPack == null)
        {
            throw new ArgumentNullException(nameof(atlasPack));
        }

        var atlasImage = atlasPack.image;
        var context = atlasPack.context;

        if (!Directory.Exists(atlasFolderPath))
        {
            Directory.CreateDirectory(atlasFolderPath);
        }

        using (var atlasStream = new FileStream(Path.Combine(atlasFolderPath, atlasFileName), FileMode.Create))
        {
            atlasStream.Write(atlasImage.imageData, 0, atlasImage.imageData.Length);
        }

        using (var contextStream = new FileStream(Path.Combine(atlasFolderPath, contextFileName), FileMode.Create))
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            JsonSerializer.Serialize(contextStream, context, options);
        }
    }

    private static AtlasImage PackImage(AtlasContext context, IReadOnlyDictionary<string, Image> imageMap)
    {
        var bounds = context.bounds;
        using var atlasImage = new Image<Rgba32>(bounds.width, bounds.height);

        foreach (var (imageName, rect) in context.imageRectMap)
        {
            var image = imageMap[imageName];
            var point = new Point(rect.x, rect.y);
            atlasImage.Mutate(ctx => ctx.DrawImage(image, point, 1));
        }

        using var atlasStream = new MemoryStream();
        atlasImage.Save(atlasStream, PngFormat.Instance);
        return new AtlasImage(atlasStream.ToArray());
    }

    private static IReadOnlyDictionary<string, Image> LoadImages(ReadOnlySpan<string> imagePaths)
    {
        var images = new Dictionary<string, Image>();
        foreach (var path in imagePaths)
        {
            var name = Path.GetFileName(path);
            var image = Image.Load(path);
            images.Add(name, image);
        }

        return images;
    }

    private static readonly List<KeyValuePair<string, Image>> imageArrayCache = new();

    private static void GetRectPackResult(IReadOnlyDictionary<string, Image> imageMap, out AtlasContext atlasContext)
    {
        var rectangles = new PackingRectangle[imageMap.Count];

        imageArrayCache.Clear();
        imageArrayCache.AddRange(imageMap);
        var images = imageArrayCache;
        for (var i = 0; i < images.Count; i++)
        {
            var image = images[i].Value;
            rectangles[i] = new PackingRectangle(0, 0, (uint)image.Width, (uint)image.Height, i);
        }

        RectanglePacker.Pack(rectangles, out var bounds);
        Array.Sort(rectangles, (a, b) => a.Id.CompareTo(b.Id));

        var imageRectMap = new Dictionary<string, Rect>();
        for (var i = 0; i < images.Count; i++)
        {
            var imageName = images[i].Key;
            var image = images[i].Value;
            var point = new Point((int)rectangles[i].X, (int)rectangles[i].Y);
            imageRectMap[imageName] = new Rect(point.X, point.Y, image.Width, image.Height);
        }

        var atlasSize = new Size((int)bounds.Width, (int)bounds.Height);
        atlasContext = new AtlasContext
        {
            bounds = atlasSize,
            imageRectMap = imageRectMap
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void CreateFileDirectoryIfNotExist(string filePath)
    {
        var fileInfo = new FileInfo(filePath);
        if (fileInfo.Directory != null && !fileInfo.Directory.Exists)
        {
            fileInfo.Directory.Create();
        }
    }
}
