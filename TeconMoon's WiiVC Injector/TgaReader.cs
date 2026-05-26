using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace TeconMoon_s_WiiVC_Injector
{
    public static class TgaReader
    {
        public static Bitmap LoadTga(string filePath)
        {
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                return LoadTga(stream);
            }
        }

        public static Bitmap LoadTga(Stream stream)
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

                Bitmap bitmap = new Bitmap(width, height, bpp == 32 ? PixelFormat.Format32bppArgb : PixelFormat.Format24bppRgb);
                BitmapData bmpData = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, bitmap.PixelFormat);

                int bytesPerPixel = bpp / 8;
                int stride = bmpData.Stride;
                IntPtr scan0 = bmpData.Scan0;

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

                // Copy to BitmapData, handling bottom-to-top layout by default in TGA
                unsafe
                {
                    byte* destPtr = (byte*)scan0.ToPointer();
                    fixed (byte* srcPtr = pixelData)
                    {
                        for (int y = 0; y < height; y++)
                        {
                            int srcY = topToBottom ? y : (height - 1 - y);
                            byte* srcRow = srcPtr + (srcY * width * bytesPerPixel);
                            byte* destRow = destPtr + (y * stride);

                            System.Buffer.MemoryCopy(srcRow, destRow, (ulong)stride, (ulong)(width * bytesPerPixel));
                        }
                    }
                }

                bitmap.UnlockBits(bmpData);
                return bitmap;
            }
        }

        public static void SaveAsTga(Image image, string filePath, int width, int height, int bpp)
        {
            using (Bitmap bmp = new Bitmap(image, new Size(width, height)))
            {
                SaveAsTga(bmp, filePath, bpp);
            }
        }

        public static void SaveAsTga(Bitmap bmp, string filePath, int bpp)
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

                BitmapData bmpData = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, bpp == 32 ? PixelFormat.Format32bppArgb : PixelFormat.Format24bppRgb);
                
                int bytesPerPixel = bpp / 8;
                int stride = bmpData.Stride;
                IntPtr scan0 = bmpData.Scan0;

                byte[] pixelData = new byte[bmp.Width * bmp.Height * bytesPerPixel];

                unsafe
                {
                    byte* srcPtr = (byte*)scan0.ToPointer();
                    fixed (byte* destPtr = pixelData)
                    {
                        for (int y = 0; y < bmp.Height; y++)
                        {
                            // TGA is bottom-to-top by default, so we reverse the row index
                            int srcY = bmp.Height - 1 - y;
                            byte* srcRow = srcPtr + (srcY * stride);
                            byte* destRow = destPtr + (y * bmp.Width * bytesPerPixel);

                            System.Buffer.MemoryCopy(srcRow, destRow, (ulong)(bmp.Width * bytesPerPixel), (ulong)(bmp.Width * bytesPerPixel));
                        }
                    }
                }

                bmp.UnlockBits(bmpData);
                writer.Write(pixelData);
            }
        }
    }
}
