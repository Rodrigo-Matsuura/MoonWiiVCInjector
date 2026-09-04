using SkiaSharp;

namespace Moon_WiiVC_Injector;

public static class TgaReader
{
    public static SKBitmap LoadTga(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        return LoadTga(stream);
    }

    public static SKBitmap LoadTga(Stream stream)
    {
        SKBitmap? bitmap = null;
        try
        {
            using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
            byte idLength = reader.ReadByte();
            byte colorMapType = reader.ReadByte();
            byte imageType = reader.ReadByte();

            // Skip color map specification
            reader.ReadBytes(5);

            short xOrigin = reader.ReadInt16();
            short yOrigin = reader.ReadInt16();
            short width = reader.ReadInt16();
            short height = reader.ReadInt16();
            byte bpp = reader.ReadByte();
            byte descriptor = reader.ReadByte();

            if (idLength > 0)
            {
                reader.ReadBytes(idLength);
            }

            // Image type 2 is uncompressed true-color, image type 10 is RLE compressed true-color
            if (imageType != 2 && imageType != 10)
            {
                throw new NotSupportedException($"Only uncompressed (type 2) or RLE compressed (type 10) true-color TGA images are supported. Found type {imageType}.");
            }

            if (bpp != 24 && bpp != 32)
            {
                throw new NotSupportedException($"Only 24-bit or 32-bit TGA images are supported. Found {bpp}-bit.");
            }

            SKImageInfo info = new(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
            bitmap = new SKBitmap(info);

            int bytesPerPixel = bpp / 8;
            bool topToBottom = (descriptor & 0x20) != 0;
            int dstStride = bitmap.RowBytes;
            Span<byte> destSpan = bitmap.GetPixelSpan();

            if (imageType == 2)
            {
                // Uncompressed
                if (bytesPerPixel == 4)
                {
                    for (int y = 0; y < height; y++)
                    {
                        int targetY = topToBottom ? y : (height - 1 - y);
                        Span<byte> destRow = destSpan.Slice(targetY * dstStride, width * 4);
                        stream.ReadExactly(destRow);
                    }
                    return bitmap;
                }
                else if (bytesPerPixel == 3)
                {
                    byte[] rowBuffer = new byte[width * 3];
                    Span<byte> rowSpan = rowBuffer.AsSpan();
                    for (int y = 0; y < height; y++)
                    {
                        stream.ReadExactly(rowSpan);
                        int targetY = topToBottom ? y : (height - 1 - y);
                        Span<byte> destRow = destSpan.Slice(targetY * dstStride, width * 4);
                        for (int x = 0; x < width; x++)
                        {
                            int srcOffset = x * 3;
                            int dstOffset = x * 4;
                            destRow[dstOffset] = rowSpan[srcOffset];         // B
                            destRow[dstOffset + 1] = rowSpan[srcOffset + 1]; // G
                            destRow[dstOffset + 2] = rowSpan[srcOffset + 2]; // R
                            destRow[dstOffset + 3] = 255;                   // A
                        }
                    }
                    return bitmap;
                }
            }

            // Fallback for RLE compressed TGA (ImageType == 10)
            byte[] pixelData = new byte[width * height * bytesPerPixel];
            if (imageType == 10)
            {
                int totalPixels = width * height;
                int pixelCount = 0;
                int offset = 0;

                while (pixelCount < totalPixels)
                {
                    byte rleHeader = reader.ReadByte();
                    int count = (rleHeader & 0x7F) + 1;

                    if ((rleHeader & 0x80) != 0)
                    {
                        // RLE packet - repeat next pixel 'count' times
                        byte b = reader.ReadByte();
                        byte g = reader.ReadByte();
                        byte r = reader.ReadByte();
                        byte a = (bpp == 32) ? reader.ReadByte() : (byte)255;

                        for (int i = 0; i < count && pixelCount < totalPixels; i++)
                        {
                            pixelData[offset] = b;
                            pixelData[offset + 1] = g;
                            pixelData[offset + 2] = r;
                            if (bpp == 32)
                            {
                                pixelData[offset + 3] = a;
                            }
                            offset += bytesPerPixel;
                            pixelCount++;
                        }
                    }
                    else
                    {
                        // Raw packet - read next 'count' pixels raw
                        for (int i = 0; i < count && pixelCount < totalPixels; i++)
                        {
                            pixelData[offset] = reader.ReadByte();
                            pixelData[offset + 1] = reader.ReadByte();
                            pixelData[offset + 2] = reader.ReadByte();
                            if (bpp == 32)
                            {
                                pixelData[offset + 3] = reader.ReadByte();
                            }
                            offset += bytesPerPixel;
                            pixelCount++;
                        }
                    }
                }
            }

            // Copy RLE pixelData to SKBitmap destination memory using Spans
            ReadOnlySpan<byte> srcSpan = pixelData.AsSpan();
            for (int y = 0; y < height; y++)
            {
                int srcY = topToBottom ? y : (height - 1 - y);
                ReadOnlySpan<byte> srcRow = srcSpan.Slice(srcY * width * bytesPerPixel, width * bytesPerPixel);
                Span<byte> destRow = destSpan.Slice(y * dstStride, width * 4);

                if (bytesPerPixel == 4)
                {
                    // Direct copy for 32-bit TGA (BGRA to BGRA)
                    srcRow.CopyTo(destRow);
                }
                else
                {
                    // Convert 24-bit BGR to 32-bit BGRA
                    for (int x = 0; x < width; x++)
                    {
                        int srcOffset = x * 3;
                        int dstOffset = x * 4;
                        destRow[dstOffset] = srcRow[srcOffset];         // B
                        destRow[dstOffset + 1] = srcRow[srcOffset + 1]; // G
                        destRow[dstOffset + 2] = srcRow[srcOffset + 2]; // R
                        destRow[dstOffset + 3] = 255;                   // A
                    }
                }
            }

            return bitmap;
        }
        catch
        {
            bitmap?.Dispose();
            throw;
        }
    }

    public static void SaveAsTga(SKBitmap image, string filePath, int width, int height, int bpp)
    {
        // Resize image to width and height using high-quality cubic filtering
        using SKBitmap resized = new(width, height);
        if (image.ScalePixels(resized, new SKSamplingOptions(SKCubicResampler.Mitchell)))
        {
            SaveAsTga(resized, filePath, bpp);
        }
        else
        {
            // Fallback using canvas drawing
            using SKCanvas canvas = new(resized);
            canvas.Clear(SKColors.Transparent);
            using var skImage = SKImage.FromBitmap(image);
            canvas.DrawImage(skImage, new SKRect(0, 0, width, height), new SKSamplingOptions(SKCubicResampler.Mitchell));
            SaveAsTga(resized, filePath, bpp);
        }
    }

    public static void SaveAsTga(SKBitmap bmp, string filePath, int bpp)
    {
        if (bpp != 24 && bpp != 32)
        {
            throw new ArgumentException("Only 24-bit or 32-bit TGA output is supported.", nameof(bpp));
        }

        using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(stream);

        // 18-byte header
        writer.Write((byte)0); // ID length
        writer.Write((byte)0); // Color map type
        writer.Write((byte)2); // Image type (uncompressed true-color)

        // Color map specification (5 bytes)
        writer.Write((ushort)0); // First entry index
        writer.Write((ushort)0); // Color map length
        writer.Write((byte)0); // Color map entry size

        // Image specification
        writer.Write((ushort)0); // X-origin
        writer.Write((ushort)0); // Y-origin
        writer.Write((ushort)bmp.Width);
        writer.Write((ushort)bmp.Height);
        writer.Write((byte)bpp);
        writer.Write((byte)0); // Descriptor (0 = bottom-to-top layout)

        int bytesPerPixel = bpp / 8;
        byte[] pixelData = new byte[bmp.Width * bmp.Height * bytesPerPixel];

        ReadOnlySpan<byte> srcBytes = bmp.GetPixelSpan();
        int srcStride = bmp.RowBytes;
        Span<byte> destSpan = pixelData.AsSpan();

        for (int y = 0; y < bmp.Height; y++)
        {
            // TGA is bottom-to-top by default, so we reverse the row index
            int srcY = bmp.Height - 1 - y;
            ReadOnlySpan<byte> srcRow = srcBytes.Slice(srcY * srcStride, srcStride);
            Span<byte> destRow = destSpan.Slice(y * bmp.Width * bytesPerPixel, bmp.Width * bytesPerPixel);

            if (bytesPerPixel == 4)
            {
                // Direct copy BGRA/RGBA to BGRA
                if (bmp.ColorType == SKColorType.Bgra8888)
                {
                    srcRow[..(bmp.Width * 4)].CopyTo(destRow);
                }
                else if (bmp.ColorType == SKColorType.Rgba8888)
                {
                    // Convert RGBA to BGRA
                    for (int x = 0; x < bmp.Width; x++)
                    {
                        int srcOffset = x * 4;
                        int dstOffset = x * 4;
                        destRow[dstOffset] = srcRow[srcOffset + 2];     // B
                        destRow[dstOffset + 1] = srcRow[srcOffset + 1]; // G
                        destRow[dstOffset + 2] = srcRow[srcOffset];     // R
                        destRow[dstOffset + 3] = srcRow[srcOffset + 3]; // A
                    }
                }
                else
                {
                    // Fallback
                    for (int x = 0; x < bmp.Width; x++)
                    {
                        SKColor color = bmp.GetPixel(x, srcY);
                        int dstOffset = x * 4;
                        destRow[dstOffset] = color.Blue;
                        destRow[dstOffset + 1] = color.Green;
                        destRow[dstOffset + 2] = color.Red;
                        destRow[dstOffset + 3] = color.Alpha;
                    }
                }
            }
            else
            {
                    // Convert 32-bit to 24-bit BGR
                    if (bmp.ColorType == SKColorType.Bgra8888)
                    {
                        for (int x = 0; x < bmp.Width; x++)
                        {
                            int srcOffset = x * 4;
                            int dstOffset = x * 3;
                            destRow[dstOffset] = srcRow[srcOffset];         // B
                            destRow[dstOffset + 1] = srcRow[srcOffset + 1]; // G
                            destRow[dstOffset + 2] = srcRow[srcOffset + 2]; // R
                        }
                    }
                    else if (bmp.ColorType == SKColorType.Rgba8888)
                    {
                        for (int x = 0; x < bmp.Width; x++)
                        {
                            int srcOffset = x * 4;
                            int dstOffset = x * 3;
                            destRow[dstOffset] = srcRow[srcOffset + 2];     // B
                            destRow[dstOffset + 1] = srcRow[srcOffset + 1]; // G
                            destRow[dstOffset + 2] = srcRow[srcOffset];     // R
                        }
                    }
                    else
                    {
                        for (int x = 0; x < bmp.Width; x++)
                        {
                            SKColor color = bmp.GetPixel(x, srcY);
                            int dstOffset = x * 3;
                            destRow[dstOffset] = color.Blue;
                            destRow[dstOffset + 1] = color.Green;
                            destRow[dstOffset + 2] = color.Red;
                        }
                    }
                }
            }

        writer.Write(pixelData);
    }

    /// <summary>
    /// Validates whether the given file is a valid image (PNG, JPG, WebP, BMP, GIF or TGA)
    /// and retrieves its dimensions without decoding the full pixel array into memory.
    /// </summary>
    public static bool TryGetImageDimensions(string filePath, out int width, out int height, out string? errorMessage)
    {
        width = 0;
        height = 0;
        errorMessage = null;

        if (!File.Exists(filePath))
        {
            errorMessage = "File does not exist.";
            return false;
        }

        try
        {
            if (Path.GetExtension(filePath).Equals(".tga", StringComparison.OrdinalIgnoreCase))
            {
                using var stream = File.OpenRead(filePath);
                using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: false);
                if (stream.Length < 18)
                {
                    errorMessage = "TGA file is too small or header is corrupt.";
                    return false;
                }

                reader.ReadByte(); // idLength
                reader.ReadByte(); // colorMapType
                byte imageType = reader.ReadByte();
                reader.ReadBytes(5); // skip color map spec
                reader.ReadInt16(); // xOrigin
                reader.ReadInt16(); // yOrigin
                short w = reader.ReadInt16();
                short h = reader.ReadInt16();
                byte bpp = reader.ReadByte();

                if (imageType != 2 && imageType != 10)
                {
                    errorMessage = $"Unsupported TGA type {imageType}. Only uncompressed (2) or RLE (10) true-color images are supported.";
                    return false;
                }

                if (bpp != 24 && bpp != 32)
                {
                    errorMessage = $"Unsupported TGA bit depth {bpp}-bit. Only 24-bit or 32-bit images are supported.";
                    return false;
                }

                if (w <= 0 || h <= 0)
                {
                    errorMessage = $"Invalid TGA dimensions: {w}x{h}.";
                    return false;
                }

                width = w;
                height = h;
                return true;
            }
            else
            {
                using var codec = SKCodec.Create(filePath);
                if (codec == null)
                {
                    errorMessage = "File is not a valid or recognized image format.";
                    return false;
                }

                width = codec.Info.Width;
                height = codec.Info.Height;

                if (width <= 0 || height <= 0)
                {
                    errorMessage = $"Invalid image dimensions: {width}x{height}.";
                    return false;
                }

                return true;
            }
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }
}
