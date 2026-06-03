using SkiaSharp;

namespace TeconMoon_s_WiiVC_Injector
{
    public static class TgaReader
    {
        public static SKBitmap LoadTga(string filePath)
        {
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                return LoadTga(stream);
            }
        }

        public static SKBitmap LoadTga(Stream stream)
        {
            using (var reader = new BinaryReader(stream))
            {
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

                SKImageInfo info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
                SKBitmap bitmap = new SKBitmap(info);

                int bytesPerPixel = bpp / 8;
                bool topToBottom = (descriptor & 0x20) != 0;

                byte[] pixelData = new byte[width * height * bytesPerPixel];

                if (imageType == 2)
                {
                    // Uncompressed
                    int bytesToRead = width * height * bytesPerPixel;
                    int bytesRead = reader.Read(pixelData, 0, bytesToRead);
                    if (bytesRead < bytesToRead)
                    {
                        Array.Clear(pixelData, bytesRead, bytesToRead - bytesRead);
                    }
                }
                else if (imageType == 10)
                {
                    // RLE compressed
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

                // Copy to SKBitmap destination memory
                IntPtr dstPtr = bitmap.GetPixels();
                int dstStride = bitmap.RowBytes;

                unsafe
                {
                    byte* destPtr = (byte*)dstPtr.ToPointer();
                    fixed (byte* srcPtr = pixelData)
                    {
                        for (int y = 0; y < height; y++)
                        {
                            int srcY = topToBottom ? y : (height - 1 - y);
                            byte* srcRow = srcPtr + (srcY * width * bytesPerPixel);
                            byte* destRow = destPtr + (y * dstStride);

                            if (bytesPerPixel == 4)
                            {
                                // Direct copy for 32-bit TGA (BGRA to BGRA)
                                System.Buffer.MemoryCopy(srcRow, destRow, (ulong)dstStride, (ulong)(width * 4));
                            }
                            else
                            {
                                // Convert 24-bit BGR to 32-bit BGRA
                                for (int x = 0; x < width; x++)
                                {
                                    destRow[x * 4] = srcRow[x * 3];       // B
                                    destRow[x * 4 + 1] = srcRow[x * 3 + 1];   // G
                                    destRow[x * 4 + 2] = srcRow[x * 3 + 2];   // R
                                    destRow[x * 4 + 3] = 255;                 // A
                                }
                            }
                        }
                    }
                }

                return bitmap;
            }
        }

        public static void SaveAsTga(SKBitmap image, string filePath, int width, int height, int bpp)
        {
            // Resize image to width and height
            using (SKBitmap resized = new SKBitmap(width, height))
            {
                if (image.ScalePixels(resized, SKFilterQuality.High))
                {
                    SaveAsTga(resized, filePath, bpp);
                }
                else
                {
                    // Fallback using canvas drawing
                    using (SKCanvas canvas = new SKCanvas(resized))
                    {
                        canvas.Clear(SKColors.Transparent);
                        canvas.DrawBitmap(image, new SKRect(0, 0, width, height), new SKPaint { FilterQuality = SKFilterQuality.High });
                    }
                    SaveAsTga(resized, filePath, bpp);
                }
            }
        }

        public static void SaveAsTga(SKBitmap bmp, string filePath, int bpp)
        {
            if (bpp != 24 && bpp != 32)
            {
                throw new ArgumentException("Only 24-bit or 32-bit TGA output is supported.", nameof(bpp));
            }

            using (var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            using (var writer = new BinaryWriter(stream))
            {
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

                IntPtr srcPtr = bmp.GetPixels();
                int srcStride = bmp.RowBytes;

                unsafe
                {
                    byte* srcBytes = (byte*)srcPtr.ToPointer();
                    fixed (byte* destPtr = pixelData)
                    {
                        for (int y = 0; y < bmp.Height; y++)
                        {
                            // TGA is bottom-to-top by default, so we reverse the row index
                            int srcY = bmp.Height - 1 - y;
                            byte* srcRow = srcBytes + (srcY * srcStride);
                            byte* destRow = destPtr + (y * bmp.Width * bytesPerPixel);

                            if (bytesPerPixel == 4)
                            {
                                // Direct copy BGRA/RGBA to BGRA
                                if (bmp.ColorType == SKColorType.Bgra8888)
                                {
                                    System.Buffer.MemoryCopy(srcRow, destRow, (ulong)(bmp.Width * 4), (ulong)(bmp.Width * 4));
                                }
                                else if (bmp.ColorType == SKColorType.Rgba8888)
                                {
                                    // Convert RGBA to BGRA
                                    for (int x = 0; x < bmp.Width; x++)
                                    {
                                        destRow[x * 4] = srcRow[x * 4 + 2];   // B
                                        destRow[x * 4 + 1] = srcRow[x * 4 + 1]; // G
                                        destRow[x * 4 + 2] = srcRow[x * 4];     // R
                                        destRow[x * 4 + 3] = srcRow[x * 4 + 3]; // A
                                    }
                                }
                                else
                                {
                                    // Fallback
                                    for (int x = 0; x < bmp.Width; x++)
                                    {
                                        SKColor color = bmp.GetPixel(x, srcY);
                                        destRow[x * 4] = color.Blue;
                                        destRow[x * 4 + 1] = color.Green;
                                        destRow[x * 4 + 2] = color.Red;
                                        destRow[x * 4 + 3] = color.Alpha;
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
                                        destRow[x * 3] = srcRow[x * 4];       // B
                                        destRow[x * 3 + 1] = srcRow[x * 4 + 1];   // G
                                        destRow[x * 3 + 2] = srcRow[x * 4 + 2];   // R
                                    }
                                }
                                else if (bmp.ColorType == SKColorType.Rgba8888)
                                {
                                    for (int x = 0; x < bmp.Width; x++)
                                    {
                                        destRow[x * 3] = srcRow[x * 4 + 2];   // B
                                        destRow[x * 3 + 1] = srcRow[x * 4 + 1];   // G
                                        destRow[x * 3 + 2] = srcRow[x * 4];       // R
                                    }
                                }
                                else
                                {
                                    for (int x = 0; x < bmp.Width; x++)
                                    {
                                        SKColor color = bmp.GetPixel(x, srcY);
                                        destRow[x * 3] = color.Blue;
                                        destRow[x * 3 + 1] = color.Green;
                                        destRow[x * 3 + 2] = color.Red;
                                    }
                                }
                            }
                        }
                    }
                }

                writer.Write(pixelData);
            }
        }
    }
}
