using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace CurskinInstaller
{
    class BuiltCursor
    {
        public byte[] Data;
        public string Extension;
        public int Frames;
    }

    static class CursorBuilder
    {
        private const int CursorSize = 32;

        public static BuiltCursor Build(byte[] imageBytes, float[] hotspot, string name)
        {
            if (imageBytes == null || imageBytes.Length == 0)
                throw new InvalidDataException("The cursor image is empty.");
            if (IsSvg(imageBytes))
                return BuildFromSvg(imageBytes, hotspot, name);
            if (IsAvif(imageBytes))
                return BuildFromAvif(imageBytes, hotspot, name);
            return BuildFromBitmapBytes(imageBytes, hotspot, name);
        }

        public static BuiltCursor Build(CursorDef cursor, string name)
        {
            if (cursor == null)
                throw new ArgumentNullException("cursor");
            if (cursor.SvgLayers != null && cursor.SvgLayers.Count > 0)
                return BuildFromSvgLayers(cursor.SvgLayers, cursor.Hotspot, name);

            byte[] imageBytes = cursor.ImageData;
            if (imageBytes == null || imageBytes.Length == 0)
                throw new InvalidDataException("The cursor has no image data.");
            if (IsSvg(imageBytes))
                return BuildFromSvg(imageBytes, cursor.Hotspot, name);
            if (IsAvif(imageBytes))
                return BuildFromAvif(imageBytes, cursor.Hotspot, name);
            return BuildFromBitmapBytes(imageBytes, cursor.Hotspot, name);
        }

        private static BuiltCursor BuildFromBitmapBytes(byte[] imageBytes, float[] hotspot, string name)
        {
            using (MemoryStream stream = new MemoryStream(imageBytes))
            using (Image image = Image.FromStream(stream))
            {
                FrameDimension dimension = null;
                if (image.FrameDimensionsList != null && image.FrameDimensionsList.Length > 0)
                    dimension = new FrameDimension(image.FrameDimensionsList[0]);

                int count = 1;
                if (dimension != null)
                {
                    try
                    {
                        count = image.GetFrameCount(dimension);
                    }
                    catch
                    {
                        count = 1;
                    }
                }
                if (count < 1) count = 1;
                if (count > 200) count = 200;

                int[] delays = GetDelays(image, count);
                List<Bitmap> frames = new List<Bitmap>(count);

                try
                {
                    for (int i = 0; i < count; i++)
                    {
                        if (dimension != null)
                            image.SelectActiveFrame(dimension, i);

                        frames.Add(new Bitmap(image));
                    }

                    return BuildFromFrames(frames, delays, hotspot, name);
                }
                finally
                {
                    foreach (Bitmap frame in frames)
                        frame.Dispose();
                }
            }
        }

        private static BuiltCursor BuildFromFrames(List<Bitmap> frames, int[] delays, float[] hotspot, string name)
        {
            if (frames == null || frames.Count == 0)
                throw new InvalidDataException("The cursor has no image frames.");

            int count = frames.Count;
            if (count > 200)
                count = 200;

            List<byte[]> encoded = new List<byte[]>(count);
            for (int i = 0; i < count; i++)
            {
                int hotX;
                int hotY;
                encoded.Add(EncodeFrame(frames[i], hotspot, out hotX, out hotY));
            }

            if (encoded.Count == 1)
            {
                BuiltCursor single = new BuiltCursor();
                single.Data = encoded[0];
                single.Extension = ".cur";
                single.Frames = 1;
                return single;
            }

            int[] rate = new int[encoded.Count];
            for (int i = 0; i < rate.Length; i++)
            {
                int value = (delays != null && i < delays.Length) ? delays[i] : 6;
                if (value < 1) value = 1;
                if (value > 100) value = 100;
                rate[i] = value;
            }

            BuiltCursor animated = new BuiltCursor();
            animated.Data = EncodeAni(encoded, rate, name);
            animated.Extension = ".ani";
            animated.Frames = encoded.Count;
            return animated;
        }

        private static BuiltCursor BuildFromSvg(byte[] svg, float[] hotspot, string name)
        {
            List<byte[]> layers = new List<byte[]>();
            layers.Add(svg);
            return BuildFromSvgLayers(layers, hotspot, name);
        }

        private static BuiltCursor BuildFromSvgLayers(List<byte[]> layers, float[] hotspot, string name)
        {
            if (layers == null || layers.Count == 0)
                throw new InvalidDataException("The SVG cursor has no layers.");

            string tempDir = CreateTempDirectory();
            try
            {
                using (Bitmap canvas = new Bitmap(CursorSize, CursorSize, PixelFormat.Format32bppArgb))
                {
                    using (Graphics graphics = Graphics.FromImage(canvas))
                    {
                        graphics.CompositingMode = CompositingMode.SourceOver;

                        // The layer list in a .curskin file is top-to-bottom.
                        for (int i = layers.Count - 1; i >= 0; i--)
                        {
                            using (Bitmap layer = RenderSvg(layers[i], tempDir, i))
                                graphics.DrawImageUnscaled(layer, 0, 0);
                        }
                    }

                    List<Bitmap> frames = new List<Bitmap>();
                    frames.Add(canvas);
                    return BuildFromFrames(frames, null, hotspot, name);
                }
            }
            finally
            {
                DeleteTempDirectory(tempDir);
            }
        }

        private static BuiltCursor BuildFromAvif(byte[] avif, float[] hotspot, string name)
        {
            string tempDir = CreateTempDirectory();
            try
            {
                string input = Path.Combine(tempDir, "input.avif");
                string prefix = Path.Combine(tempDir, "frame.png");
                File.WriteAllBytes(input, avif);

                int exitCode;
                string log = ExternalTools.Run(ExternalTools.AvifDecPath,
                    "--index all " + ExternalTools.Quote(input) + " " + ExternalTools.Quote(prefix),
                    out exitCode);
                if (exitCode != 0)
                    throw new InvalidDataException("Could not decode the AVIF cursor. " + Shorten(log));

                string[] files = Directory.GetFiles(tempDir, "frame-*.png");
                if (files.Length == 0)
                {
                    string single = Path.Combine(tempDir, "frame.png");
                    if (File.Exists(single))
                        files = new string[] { single };
                }
                if (files.Length == 0)
                    throw new InvalidDataException("The AVIF cursor produced no frames.");
                Array.Sort(files, StringComparer.Ordinal);

                int[] delays = ParseAvifDelays(log, files.Length);
                List<Bitmap> frames = new List<Bitmap>(files.Length);
                try
                {
                    foreach (string file in files)
                    {
                        using (Image image = Image.FromFile(file))
                            frames.Add(new Bitmap(image));
                    }

                    return BuildFromFrames(frames, delays, hotspot, name);
                }
                finally
                {
                    foreach (Bitmap frame in frames)
                        frame.Dispose();
                }
            }
            finally
            {
                DeleteTempDirectory(tempDir);
            }
        }

        private static Bitmap RenderSvg(byte[] svg, string tempDir, int index)
        {
            string number = index.ToString(CultureInfo.InvariantCulture);
            string input = Path.Combine(tempDir, "layer" + number + ".svg");
            string output = Path.Combine(tempDir, "layer" + number + ".png");
            File.WriteAllBytes(input, svg);

            int exitCode;
            string log = ExternalTools.Run(ExternalTools.ResvgPath,
                "--width " + CursorSize.ToString(CultureInfo.InvariantCulture) +
                " --height " + CursorSize.ToString(CultureInfo.InvariantCulture) +
                " " + ExternalTools.Quote(input) + " " + ExternalTools.Quote(output),
                out exitCode);
            if (exitCode != 0 || !File.Exists(output))
                throw new InvalidDataException("Could not render the SVG cursor. " + Shorten(log));

            using (Image image = Image.FromFile(output))
                return new Bitmap(image);
        }

        private static int[] ParseAvifDelays(string info, int frameCount)
        {
            int timescale = 60;
            Match scale = Regex.Match(info ?? "", @"(\d+)\s+timescales per second");
            if (scale.Success)
            {
                int parsed;
                if (int.TryParse(scale.Groups[1].Value, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out parsed) && parsed > 0)
                    timescale = parsed;
            }

            List<int> durations = new List<int>();
            foreach (Match match in Regex.Matches(info ?? "",
                @"\[duration\s+[\d.]+\s+\((\d+)\s+timescales\)\]"))
            {
                int parsed;
                if (int.TryParse(match.Groups[1].Value, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out parsed))
                    durations.Add(parsed);
            }

            int[] delays = new int[frameCount];
            for (int i = 0; i < frameCount; i++)
            {
                int duration = 10;
                if (i < durations.Count)
                    duration = durations[i];
                else if (durations.Count > 0)
                    duration = durations[durations.Count - 1];

                int jiffies = (int)Math.Round(duration * 60.0 / timescale);
                if (jiffies < 1) jiffies = 1;
                if (jiffies > 100) jiffies = 100;
                delays[i] = jiffies;
            }
            return delays;
        }

        private static bool IsSvg(byte[] data)
        {
            if (data == null || data.Length == 0)
                return false;

            int start = 0;
            while (start < data.Length &&
                   (data[start] == 0xEF || data[start] == 0xBB || data[start] == 0xBF ||
                    data[start] == (byte)' ' || data[start] == (byte)'\t' ||
                    data[start] == (byte)'\r' || data[start] == (byte)'\n'))
                start++;

            if (start >= data.Length || data[start] != (byte)'<')
                return false;

            string header = Encoding.ASCII.GetString(data, start,
                Math.Min(1024, data.Length - start));
            return header.IndexOf("<svg", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsAvif(byte[] data)
        {
            if (data == null || data.Length < 12)
                return false;
            if (data[4] != (byte)'f' || data[5] != (byte)'t' ||
                data[6] != (byte)'y' || data[7] != (byte)'p')
                return false;

            string header = Encoding.ASCII.GetString(data, 0, Math.Min(64, data.Length));
            return header.IndexOf("avif", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   header.IndexOf("avis", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string Shorten(string value)
        {
            value = (value ?? "").Trim();
            if (value.Length > 500)
                value = value.Substring(0, 500);
            return value;
        }

        private static string CreateTempDirectory()
        {
            string path = Path.Combine(Path.GetTempPath(), "CurskinInstaller",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static void DeleteTempDirectory(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                    Directory.Delete(path, true);
            }
            catch
            {
            }
        }

        private static int[] GetDelays(Image image, int count)
        {
            int[] hundredths = new int[count];
            for (int i = 0; i < count; i++)
                hundredths[i] = 10;

            try
            {
                PropertyItem item = image.GetPropertyItem(0x5100);
                if (item != null && item.Value != null)
                {
                    for (int i = 0; i < count && (i + 1) * 4 <= item.Value.Length; i++)
                    {
                        int delay = BitConverter.ToInt32(item.Value, i * 4);
                        if (delay > 0)
                            hundredths[i] = delay;
                    }
                }
            }
            catch
            {
            }

            int[] jiffies = new int[count];
            for (int i = 0; i < count; i++)
            {
                int value = (int)Math.Round(hundredths[i] * 0.6);
                if (value < 1) value = 1;
                if (value > 100) value = 100;
                jiffies[i] = value;
            }
            return jiffies;
        }

        private static byte[] EncodeFrame(Bitmap source, float[] hotspot, out int hotX, out int hotY)
        {
            int offsetX;
            int offsetY;
            int width;
            int height;

            using (Bitmap canvas = ScaleToCanvas(source, out offsetX, out offsetY, out width, out height))
            {
                ComputeHotspot(hotspot, source.Width, source.Height, width, height,
                    offsetX, offsetY, out hotX, out hotY);
                return EncodeCur(canvas, hotX, hotY);
            }
        }

        private static Bitmap ScaleToCanvas(Bitmap source, out int offsetX, out int offsetY,
            out int width, out int height)
        {
            double scale = Math.Min((double)CursorSize / source.Width, (double)CursorSize / source.Height);
            width = Math.Max(1, (int)Math.Round(source.Width * scale));
            height = Math.Max(1, (int)Math.Round(source.Height * scale));
            if (width > CursorSize) width = CursorSize;
            if (height > CursorSize) height = CursorSize;
            offsetX = (CursorSize - width) / 2;
            offsetY = (CursorSize - height) / 2;

            Bitmap canvas = new Bitmap(CursorSize, CursorSize, PixelFormat.Format32bppArgb);
            using (Bitmap argb = ToArgb32(source))
            using (Bitmap scaled = NearestNeighbor(argb, width, height))
            using (Graphics graphics = Graphics.FromImage(canvas))
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.DrawImageUnscaled(scaled, offsetX, offsetY);
            }
            return canvas;
        }

        private static Bitmap ToArgb32(Bitmap source)
        {
            if (source.PixelFormat == PixelFormat.Format32bppArgb)
                return new Bitmap(source);

            Bitmap result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(result))
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height));
            }
            return result;
        }

        private static Bitmap NearestNeighbor(Bitmap source, int width, int height)
        {
            Bitmap result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            BitmapData sourceData = source.LockBits(new Rectangle(0, 0, source.Width, source.Height),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            BitmapData resultData = result.LockBits(new Rectangle(0, 0, width, height),
                ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

            try
            {
                byte[] row = new byte[width * 4];
                for (int y = 0; y < height; y++)
                {
                    int sourceY = (int)((y + 0.5) * source.Height / height);
                    if (sourceY >= source.Height) sourceY = source.Height - 1;
                    IntPtr sourceRow = RowPointer(sourceData, sourceY, source.Height);

                    for (int x = 0; x < width; x++)
                    {
                        int sourceX = (int)((x + 0.5) * source.Width / width);
                        if (sourceX >= source.Width) sourceX = source.Width - 1;

                        int pixel = Marshal.ReadInt32(sourceRow, sourceX * 4);
                        row[x * 4] = (byte)(pixel & 0xFF);
                        row[x * 4 + 1] = (byte)((pixel >> 8) & 0xFF);
                        row[x * 4 + 2] = (byte)((pixel >> 16) & 0xFF);
                        row[x * 4 + 3] = (byte)((pixel >> 24) & 0xFF);
                    }

                    Marshal.Copy(row, 0, RowPointer(resultData, y, height), row.Length);
                }
            }
            finally
            {
                source.UnlockBits(sourceData);
                result.UnlockBits(resultData);
            }

            return result;
        }

        private static IntPtr RowPointer(BitmapData data, int y, int height)
        {
            if (data.Stride >= 0)
                return IntPtr.Add(data.Scan0, y * data.Stride);
            return IntPtr.Add(data.Scan0, (height - 1 - y) * data.Stride);
        }

        private static void ComputeHotspot(float[] hotspot, int sourceWidth, int sourceHeight,
            int width, int height, int offsetX, int offsetY, out int hotX, out int hotY)
        {
            if (hotspot == null || hotspot.Length == 0)
            {
                hotX = offsetX;
                hotY = offsetY;
            }
            else
            {
                hotX = offsetX + HotspotPixel(hotspot[0], sourceWidth, width);
                hotY = offsetY + (hotspot.Length > 1 ? HotspotPixel(hotspot[1], sourceHeight, height) : 0);
            }

            if (hotX < 0) hotX = 0;
            if (hotX > CursorSize - 1) hotX = CursorSize - 1;
            if (hotY < 0) hotY = 0;
            if (hotY > CursorSize - 1) hotY = CursorSize - 1;
        }

        private static int HotspotPixel(float value, int sourceSize, int targetSize)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0)
                return 0;
            if (value <= 1.0f)
                return (int)Math.Round(value * targetSize);
            return (int)Math.Round(value * targetSize / sourceSize);
        }

        private static byte[] EncodeCur(Bitmap bitmap, int hotX, int hotY)
        {
            const int width = CursorSize;
            const int height = CursorSize;
            int xorLength = width * height * 4;
            int andStride = ((width + 31) / 32) * 4;
            int andLength = andStride * height;
            byte[] xor = new byte[xorLength];
            byte[] andMask = new byte[andLength];

            BitmapData data = bitmap.LockBits(new Rectangle(0, 0, width, height),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

            try
            {
                int offset = 0;
                for (int y = 0; y < height; y++)
                {
                    int sourceY = height - 1 - y;
                    IntPtr row = RowPointer(data, sourceY, height);
                    int andOffset = y * andStride;

                    for (int x = 0; x < width; x++)
                    {
                        int pixel = Marshal.ReadInt32(row, x * 4);
                        byte b = (byte)(pixel & 0xFF);
                        byte g = (byte)((pixel >> 8) & 0xFF);
                        byte r = (byte)((pixel >> 16) & 0xFF);
                        byte a = (byte)((pixel >> 24) & 0xFF);

                        if (a == 0)
                        {
                            b = 0;
                            g = 0;
                            r = 0;
                        }

                        xor[offset++] = b;
                        xor[offset++] = g;
                        xor[offset++] = r;
                        xor[offset++] = a;

                        if (a < 128)
                            andMask[andOffset + (x >> 3)] |= (byte)(0x80 >> (x & 7));
                    }
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            int imageSize = 40 + xorLength + andLength;

            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write((ushort)0);
                writer.Write((ushort)2);
                writer.Write((ushort)1);
                writer.Write((byte)width);
                writer.Write((byte)height);
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((ushort)hotX);
                writer.Write((ushort)hotY);
                writer.Write((uint)imageSize);
                writer.Write((uint)22);

                writer.Write(40);
                writer.Write(width);
                writer.Write(height * 2);
                writer.Write((ushort)1);
                writer.Write((ushort)32);
                writer.Write(0);
                writer.Write(xorLength + andLength);
                writer.Write(0);
                writer.Write(0);
                writer.Write(0);
                writer.Write(0);

                writer.Write(xor);
                writer.Write(andMask);
                writer.Flush();
                return stream.ToArray();
            }
        }

        private static byte[] EncodeAni(List<byte[]> frames, int[] delays, string name)
        {
            byte[] anih = new byte[36];
            using (MemoryStream stream = new MemoryStream(anih))
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(36);
                writer.Write(frames.Count);
                writer.Write(frames.Count);
                writer.Write(0);
                writer.Write(0);
                writer.Write(0);
                writer.Write(0);
                writer.Write(6);
                writer.Write(1);
            }

            byte[] rate = new byte[delays.Length * 4];
            for (int i = 0; i < delays.Length; i++)
                Buffer.BlockCopy(BitConverter.GetBytes(delays[i]), 0, rate, i * 4, 4);

            byte[] info = Chunk("INFO", AsciiZ(name));
            byte[] frameBytes;
            using (MemoryStream frameStream = new MemoryStream())
            {
                foreach (byte[] frame in frames)
                {
                    byte[] icon = Chunk("icon", frame);
                    frameStream.Write(icon, 0, icon.Length);
                }
                frameBytes = frameStream.ToArray();
            }

            byte[] frameList = ListChunk("fram", frameBytes);
            byte[] anihChunk = Chunk("anih", anih);
            byte[] rateChunk = Chunk("rate", rate);

            using (MemoryStream body = new MemoryStream())
            {
                body.Write(anihChunk, 0, anihChunk.Length);
                body.Write(rateChunk, 0, rateChunk.Length);
                body.Write(info, 0, info.Length);
                body.Write(frameList, 0, frameList.Length);

                byte[] bodyBytes = body.ToArray();
                byte[] payload = new byte[4 + bodyBytes.Length];
                Encoding.ASCII.GetBytes("ACON", 0, 4, payload, 0);
                Buffer.BlockCopy(bodyBytes, 0, payload, 4, bodyBytes.Length);
                return Chunk("RIFF", payload);
            }
        }

        private static byte[] AsciiZ(string value)
        {
            if (string.IsNullOrEmpty(value))
                value = "cursors";
            byte[] raw = Encoding.ASCII.GetBytes(value);
            byte[] result = new byte[raw.Length + 1];
            Array.Copy(raw, result, raw.Length);
            return result;
        }

        private static byte[] ListChunk(string type, byte[] data)
        {
            byte[] payload = new byte[4 + data.Length];
            Encoding.ASCII.GetBytes(type, 0, 4, payload, 0);
            Buffer.BlockCopy(data, 0, payload, 4, data.Length);
            return Chunk("LIST", payload);
        }

        private static byte[] Chunk(string id, byte[] data)
        {
            int padding = data.Length & 1;
            byte[] result = new byte[8 + data.Length + padding];
            Encoding.ASCII.GetBytes(id, 0, 4, result, 0);
            result[4] = (byte)(data.Length & 0xFF);
            result[5] = (byte)((data.Length >> 8) & 0xFF);
            result[6] = (byte)((data.Length >> 16) & 0xFF);
            result[7] = (byte)((data.Length >> 24) & 0xFF);
            Buffer.BlockCopy(data, 0, result, 8, data.Length);
            return result;
        }
    }
}
