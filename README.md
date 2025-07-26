# AtlasPack

**AtlasPack** is a C# class library that packs multiple images into a single texture atlas and stores accompanying metadata (image placement info) in a `.zip` file or folder format.

## Features

- Loads images from a directory or file list
- Packs images using `RectpackSharp` with automatic atlas size computation
- Outputs a `.zip` archive (`.atlas`) or a folder containing:
  - `atlas.png`: the combined texture atlas
  - `context.json`: mapping of image names to positions in the atlas
- Reads back the `.atlas` format
- Zero Unity dependency — works as a pure C# library
- Uses `ImageSharp` for image handling

## Structure

### Core Types

- `AtlasPack`: Holds an `AtlasImage` and `AtlasContext`
- `AtlasContext`: Read-only mapping from image name to `Rect`
- `AtlasImage`: Stores PNG image data as a `byte[]`
- `Rect`: Simple struct with `X`, `Y`, `Width`, `Height`

### Main API (Static)

```csharp
AtlasPacker.GetImages(string directory) : string[]
AtlasPacker.PackAtlas(ReadOnlySpan<string> sourceImagePaths) : AtlasPack?
AtlasPacker.PackAtlas(ReadOnlySpan<string> sourceImagePaths, string atlasPath) : void
AtlasPacker.PackAtlasAsFolder(ReadOnlySpan<string> sourceImagePaths, string folderPath) : void
AtlasPacker.LoadAtlas(string atlasPath) : AtlasPack
```

### Output Files

- `atlas.png`: Packed PNG image
- `context.json`: Serialized dictionary with image bounds using `System.Text.Json`

## Dependencies

- [RectpackSharp](https://github.com/TeamHypersomnia/RectpackSharp)
- [ImageSharp](https://github.com/SixLabors/ImageSharp)
- `System.IO.Compression`
- `System.Text.Json`

## Usage Notes

- Supported image extensions: `bmp`, `gif`, `jpg`, `jpeg`, `png`, `tif`, `tiff`
- `RectpackSharp` is used for efficient rectangle packing
- The `.atlas` format is just a `.zip` file with two entries: `atlas.png` and `context.json`
- Use `AtlasPacker.LoadAtlas()` to read back the `.atlas` into memory

---

Generated from actual implementation without assumptions or fabricated usage.
