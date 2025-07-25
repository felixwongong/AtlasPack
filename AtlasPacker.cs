using System.Collections;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json;
using RectpackSharp;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

public struct Rect {
    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }

    public Rect(int x, int y, int width, int height) {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }
}

public class AtlasContext: IEnumerable<KeyValuePair<string, Rect>> {
    public IReadOnlyDictionary<string, Rect> imageRectMap { get; }
    
    public AtlasContext(IReadOnlyDictionary<string, Rect> imageRectMap) {
        this.imageRectMap = imageRectMap;
    }

    public IEnumerator<KeyValuePair<string, Rect>> GetEnumerator() {
        return imageRectMap.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() {
        return GetEnumerator();
    }
}

public class AtlasPack {
    public AtlasContext context;
    public AtlasImage image;
    
    public AtlasPack(AtlasContext context, AtlasImage image) {
        this.context = context;
        this.image = image;
    }
}

public struct AtlasImage(byte[] imageData) {
    public byte[] imageData = imageData;
}

public static partial class AtlasPacker {
    private static readonly string[] imageExtensions = new[] { "*.bmp", "*.gif", "*.jpg", "*.jpeg", "*.png", "*.tif", "*.tiff" };
    private static readonly string atlasFileName = "atlas.png";
    private static readonly string contextFileName = "context.json";

    /// <param name="directory"></param>
    /// <returns>Full Name of all images in the directory</returns>
    public static IReadOnlyList<string> GetImages(string directory) {
        var directoryInfo = new DirectoryInfo(directory);
        if (!directoryInfo.Exists) {
            return Array.Empty<string>();
        }
        var imagePaths = new List<string>();
        foreach (var extension in imageExtensions) {
            var files = directoryInfo.GetFiles(extension, SearchOption.AllDirectories);
            imagePaths.AddRange(files.Select(x => x.FullName));
        }

        return imagePaths;
    }
    
    public static int PackAtlas(IReadOnlyList<string> sourceImagePaths, out AtlasPack? atlasPack) {
        atlasPack = null;
        if (sourceImagePaths.Count <= 0) {
            return 0;
        }

        var imageMap = LoadImages(sourceImagePaths);
        if (imageMap.Count == 0) {
            return 0;
        }

        var packedCount = GetRectPackResult(imageMap, out var atlasContext, out var bounds);
        if (packedCount <= 0) {
            return 0;
        }
        
        var atlasImage = PackImage(atlasContext, imageMap, bounds);
        atlasPack = new AtlasPack(atlasContext, atlasImage);
        return packedCount;
    }

    public static int PackAtlas(IReadOnlyList<string> sourceImagePath, string atlasPath) {
        var packedCount = PackAtlas(sourceImagePath, out var atlasPack);
        if (atlasPack == null) {
            return 0;
        }
        
        CreateFileDirectoryIfNotExist(atlasPath);
        
        using var archiveStream = new FileStream(atlasPath, FileMode.OpenOrCreate);
        SavePackAsAtlas(atlasPack, archiveStream);

        return packedCount;
    }
    
    public static int PackAtlasAsFolder(IReadOnlyList<string> sourceImagePath, string folderPath) {
        var packedCount = PackAtlas(sourceImagePath, out var atlasPack);
        if (atlasPack == null) {
            return 0;
        }

        if (!Directory.Exists(folderPath)) {
            Directory.CreateDirectory(folderPath);
        }
        
        SavePackAsFolder(atlasPack, folderPath);

        return packedCount;
    }

    public static void SavePackAsAtlas(AtlasPack atlasPack, Stream stream) {
        if (atlasPack == null) {
            throw new ArgumentNullException(nameof(atlasPack));
        }

        if (stream == null) {
            throw new ArgumentNullException(nameof(stream));
        }

        var atlasImage = atlasPack.image;
        var context = atlasPack.context;

        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, false);
        
        var atlasEntry = archive.CreateEntry(atlasFileName);
        using (var entryStream = atlasEntry.Open()) {
            entryStream.Write(atlasImage.imageData, 0, atlasImage.imageData.Length);
        }
        
        var contextEntry = archive.CreateEntry(contextFileName);
        using (var entryStream = contextEntry.Open()) {
            var options = new JsonSerializerOptions {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            JsonSerializer.Serialize(entryStream, context.imageRectMap, options);
        }
    }

    public static AtlasPack LoadAtlas(string atlasPath) {
        if (string.IsNullOrEmpty(atlasPath)) {
            throw new ArgumentNullException(nameof(atlasPath));
        }

        if (!File.Exists(atlasPath)) {
            throw new FileNotFoundException("Atlas file not found.", atlasPath);
        }

        using var archive = ZipFile.OpenRead(atlasPath);
        var atlasEntry = archive.GetEntry(atlasFileName);
        if (atlasEntry == null) {
            throw new FileNotFoundException("Atlas image not found in the archive.");
        }

        using var atlasStream = atlasEntry.Open();
        var atlasImageData = new byte[atlasEntry.Length];
        var offset = 0;
        var remaining = atlasImageData.Length;

        while (remaining > 0) {
            int bytesRead = atlasStream.Read(atlasImageData, offset, remaining);
            if (bytesRead == 0) {
                throw new EndOfStreamException("Unexpected end of stream while reading the atlas image data.");
            }

            offset += bytesRead;
            remaining -= bytesRead;
        }

        var contextEntry = archive.GetEntry(contextFileName);
        if (contextEntry == null) {
            throw new FileNotFoundException("Context file not found in the archive.");
        }

        using var contextStream = contextEntry.Open();
        var context = JsonSerializer.Deserialize<Dictionary<string, Rect>>(contextStream);

        if (context == null) {
            throw new InvalidOperationException("Failed to deserialize context from the archive.");
        }

        var atlasContext = new AtlasContext(context);
        var atlasImage = new AtlasImage(atlasImageData);
        
        return new AtlasPack(atlasContext, atlasImage);
    }

    public static void SavePackAsFolder(AtlasPack atlasPack, string atlasFolderPath) {
        if (atlasPack == null) {
            throw new ArgumentNullException(nameof(atlasPack));
        }

        var atlasImage = atlasPack.image;
        var context = atlasPack.context;

        if (!Directory.Exists(atlasFolderPath)) {
            Directory.CreateDirectory(atlasFolderPath);
        }
        
        using (var atlasStream = new FileStream(Path.Combine(atlasFolderPath, atlasFileName), FileMode.Create)) {
            atlasStream.Write(atlasImage.imageData, 0, atlasImage.imageData.Length);
        }
        
        using (var contextStream = new FileStream(Path.Combine(atlasFolderPath, contextFileName), FileMode.Create)) {
            var options = new JsonSerializerOptions {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            JsonSerializer.Serialize(contextStream, context.imageRectMap, options);
        }
    }

    private static AtlasImage PackImage(AtlasContext context, IReadOnlyDictionary<string, Image> imageMap, Size bounds) {
        using var atlasImage = new Image<Rgba32>(bounds.Width, bounds.Height);

        foreach (var (imageName, rect) in context.imageRectMap) {
            var image = imageMap[imageName];
            var point = new Point(rect.X, rect.Y);
            atlasImage.Mutate(ctx => ctx.DrawImage(image, point, 1));
        }

        using var atlasStream = new MemoryStream();
        atlasImage.Save(atlasStream, PngFormat.Instance);
        return new AtlasImage(atlasStream.ToArray());
    }

    private static IReadOnlyDictionary<string, Image> LoadImages(IEnumerable<string> imagePaths)
    {
        var images = new Dictionary<string, Image>();
        foreach (var path in imagePaths) {
            var name = Path.GetFileName(path);
            var image = Image.Load(path);
            images.Add(name, image);
        }

        return images;
    }

    private static readonly List<KeyValuePair<string, Image>> imageArrayCache = new();
    private static int GetRectPackResult(IReadOnlyDictionary<string, Image> imageMap, out AtlasContext atlasContext, out Size atlasSize)
    {
        var rectangles = new PackingRectangle[imageMap.Count];

        imageArrayCache.Clear();
        imageArrayCache.AddRange(imageMap);
        var images = imageArrayCache;
        for (var i = 0; i < images.Count; i++) {
            var image = images[i].Value;
            rectangles[i] = new PackingRectangle(0, 0, (uint)image.Width, (uint)image.Height, i);
        }

        RectanglePacker.Pack(rectangles.AsSpan(), out var bounds);
        Array.Sort(rectangles, (a, b) => a.Id.CompareTo(b.Id));
        
        var imageRectMap = new Dictionary<string, Rect>();
        for (var i = 0; i < images.Count; i++)
        {
            var imageName = images[i].Key;
            var image = images[i].Value;
            var point = new Point((int)rectangles[i].X, (int)rectangles[i].Y);
            imageRectMap[imageName] = new Rect(point.X, point.Y, image.Width, image.Height);
        }
        atlasSize = new Size((int)bounds.Width, (int)bounds.Height);

        atlasContext = new AtlasContext(imageRectMap);
        return rectangles.Length;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void CreateFileDirectoryIfNotExist(string filePath) {
        var fileInfo = new FileInfo(filePath);
        if (fileInfo.Directory != null && !fileInfo.Directory.Exists) {
            fileInfo.Directory.Create();
        }
    }
}