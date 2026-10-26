using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CurskinInstaller
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);

            if (args.Length >= 2 && string.Equals(args[0], "--install", StringComparison.OrdinalIgnoreCase))
            {
                Environment.ExitCode = RunHeadless(args);
                return;
            }

            if (args.Length >= 1 && string.Equals(args[0], "--restore", StringComparison.OrdinalIgnoreCase))
            {
                Environment.ExitCode = RunRestore();
                return;
            }

            if (args.Length >= 1 && string.Equals(args[0], "--reapply", StringComparison.OrdinalIgnoreCase))
            {
                Environment.ExitCode = RunReapply();
                return;
            }

            string initial = null;
            if (args.Length >= 1 && File.Exists(args[0]))
                initial = args[0];

            System.Windows.Forms.Application.Run(new MainForm(initial));
        }

        static int RunRestore()
        {
            try
            {
                Console.WriteLine(Installer.RestorePrevious());
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ERROR: " + ex.Message);
                return 1;
            }
        }

        // Runs from the per-user Run key after sign-in. Windows can load its own
        // cursors during logon even when the custom scheme is still selected, so
        // re-apply a few times to cover late cursor initialization.
        static int RunReapply()
        {
            int[] delays = { 0, 3000, 7000, 15000 };
            int applied = 0;

            foreach (int delay in delays)
            {
                if (delay > 0)
                    System.Threading.Thread.Sleep(delay);

                try
                {
                    applied = Math.Max(applied, Installer.ReapplyCurrentScheme());
                }
                catch
                {
                }
            }

            return applied > 0 ? 0 : 1;
        }

        static int RunHeadless(string[] args)
        {
            try
            {
                string path = args[1];
                CurskinSkin skin = CurskinParser.ParseFile(path);
                List<CurskinVariant> selected = new List<CurskinVariant>();
                foreach (CurskinVariant v in skin.Variants)
                {
                    if (v.Cursors.Count > 0)
                        selected.Add(v);
                }

                if (selected.Count == 0)
                {
                    Console.Error.WriteLine("No cursors were found in this .curskin file.");
                    return 2;
                }

                bool apply = true;
                bool mixed = selected.Count > 1;
                bool keep = false;
                for (int i = 2; i < args.Length; i++)
                {
                    if (string.Equals(args[i], "--no-apply", StringComparison.OrdinalIgnoreCase))
                        apply = false;
                    if (string.Equals(args[i], "--no-mixed", StringComparison.OrdinalIgnoreCase))
                        mixed = false;
                    if (string.Equals(args[i], "--startup", StringComparison.OrdinalIgnoreCase))
                        keep = true;
                    if (string.Equals(args[i], "--no-startup", StringComparison.OrdinalIgnoreCase))
                        keep = false;
                }

                InstallResult result = Installer.Install(path, skin, selected, mixed, apply, keep,
                    delegate(string message) { Console.WriteLine(message); });

                Console.WriteLine("OK");
                foreach (string scheme in result.SchemeNames)
                    Console.WriteLine("Scheme: " + scheme);
                Console.WriteLine("Applied: " + (result.AppliedScheme == null ? "(none)" : result.AppliedScheme));
                Console.WriteLine("Folder: " + result.InstallDir);
                Console.WriteLine("Files: " + result.FileCount.ToString());
                if (result.ThemeSynced)
                    Console.WriteLine("Theme: " + result.ThemeFile);
                if (result.ThemeError != null)
                    Console.WriteLine("Theme warning: " + result.ThemeError);
                if (result.StartupRegistered)
                    Console.WriteLine("Startup: cursors will be re-applied after sign-in");
                if (result.StartupError != null)
                    Console.WriteLine("Startup warning: " + result.StartupError);
                if (result.MouseXRunning)
                    Console.WriteLine("Note: Mouse X is running.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ERROR: " + ex.Message);
                Console.Error.WriteLine(ex.ToString());
                return 1;
            }
        }
    }

    class CurskinSkin
    {
        public string Name = "";
        public string Author = "";
        public string Description = "";
        public string SourcePath = "";
        public List<CurskinVariant> Variants = new List<CurskinVariant>();
    }

    class CurskinVariant
    {
        public string Name = "";
        public int Index;
        public Dictionary<string, CursorDef> Cursors =
            new Dictionary<string, CursorDef>(StringComparer.OrdinalIgnoreCase);
    }

    class CursorDef
    {
        public string Type = "";
        public byte[] ImageData;
        public int ImageFormat;
        public List<byte[]> SvgLayers = new List<byte[]>();
        public float[] Hotspot = new float[0];
    }

    class ProtoReader
    {
        private readonly byte[] data;
        private int position;

        public ProtoReader(byte[] data)
        {
            this.data = data;
        }

        public bool HasMore
        {
            get { return position < data.Length; }
        }

        public void ReadTag(out int field, out int wire)
        {
            ulong tag = ReadVarint();
            field = (int)(tag >> 3);
            wire = (int)(tag & 7);
            if (field <= 0)
                throw new InvalidDataException("This file does not look like a valid .curskin file.");
        }

        public ulong ReadVarint()
        {
            ulong result = 0;
            int shift = 0;
            while (position < data.Length)
            {
                byte b = data[position++];
                result |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                    return result;
                shift += 7;
                if (shift > 63)
                    throw new InvalidDataException("Invalid .curskin data.");
            }
            throw new EndOfStreamException("Unexpected end of .curskin data.");
        }

        public int ReadLength()
        {
            ulong length = ReadVarint();
            if (length > int.MaxValue || position + (int)length > data.Length)
                throw new InvalidDataException("Invalid .curskin data.");
            return (int)length;
        }

        public byte[] ReadBytes()
        {
            int length = ReadLength();
            byte[] result = new byte[length];
            Buffer.BlockCopy(data, position, result, 0, length);
            position += length;
            return result;
        }

        public string ReadString()
        {
            return Encoding.UTF8.GetString(ReadBytes());
        }

        public float ReadFloat()
        {
            if (position + 4 > data.Length)
                throw new EndOfStreamException("Unexpected end of .curskin data.");
            float value = BitConverter.ToSingle(data, position);
            position += 4;
            return value;
        }

        public void Skip(int wire)
        {
            switch (wire)
            {
                case 0:
                    ReadVarint();
                    break;
                case 1:
                    position += 8;
                    break;
                case 2:
                    int length = ReadLength();
                    position += length;
                    break;
                case 5:
                    position += 4;
                    break;
                default:
                    throw new InvalidDataException("Unsupported .curskin field.");
            }
            if (position > data.Length)
                throw new EndOfStreamException("Unexpected end of .curskin data.");
        }
    }

    static class CurskinParser
    {
        public static CurskinSkin ParseFile(string path)
        {
            CurskinSkin skin = Parse(File.ReadAllBytes(path));
            skin.SourcePath = path;
            if (string.IsNullOrWhiteSpace(skin.Name))
                skin.Name = FallbackName(path);
            return skin;
        }

        public static string FallbackName(string path)
        {
            string fileName = Path.GetFileNameWithoutExtension(path);
            if (!string.IsNullOrWhiteSpace(fileName) &&
                !string.Equals(fileName, "skin", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(fileName, "curskin", StringComparison.OrdinalIgnoreCase))
                return fileName;

            try
            {
                DirectoryInfo parent = Directory.GetParent(Path.GetFullPath(path));
                if (parent != null && !string.IsNullOrWhiteSpace(parent.Name))
                    return parent.Name;
            }
            catch
            {
            }

            return string.IsNullOrWhiteSpace(fileName) ? "Curskin" : fileName;
        }

        public static CurskinSkin Parse(byte[] data)
        {
            CurskinSkin skin = new CurskinSkin();
            ProtoReader reader = new ProtoReader(data);

            while (reader.HasMore)
            {
                int field;
                int wire;
                reader.ReadTag(out field, out wire);

                if (field == 1 && wire == 2)
                    skin.Variants.Add(ParseVariant(reader.ReadBytes()));
                else if (field == 2 && wire == 2)
                    skin.Name = reader.ReadString();
                else if (field == 3 && wire == 2)
                    skin.Author = reader.ReadString();
                else if (field == 5 && wire == 2)
                    skin.Description = reader.ReadString();
                else
                    reader.Skip(wire);
            }

            for (int i = 0; i < skin.Variants.Count; i++)
            {
                skin.Variants[i].Index = i;
                if (string.IsNullOrWhiteSpace(skin.Variants[i].Name))
                    skin.Variants[i].Name = "Variant " + (i + 1).ToString();
            }

            return skin;
        }

        static CurskinVariant ParseVariant(byte[] data)
        {
            CurskinVariant variant = new CurskinVariant();
            ProtoReader reader = new ProtoReader(data);

            while (reader.HasMore)
            {
                int field;
                int wire;
                reader.ReadTag(out field, out wire);

                if (field == 1 && wire == 2)
                {
                    variant.Name = reader.ReadString();
                }
                else if (field == 4 && wire == 2)
                {
                    CursorDef cursor = ParseCursor(reader.ReadBytes());
                    if (cursor != null &&
                        ((cursor.ImageData != null && cursor.ImageData.Length > 0) ||
                         cursor.SvgLayers.Count > 0))
                    {
                        string key = NormalizeType(cursor.Type);
                        cursor.Type = key;
                        if (!variant.Cursors.ContainsKey(key))
                            variant.Cursors.Add(key, cursor);
                    }
                }
                else
                {
                    reader.Skip(wire);
                }
            }

            return variant;
        }

        static CursorDef ParseCursor(byte[] data)
        {
            CursorDef cursor = new CursorDef();
            ProtoReader reader = new ProtoReader(data);

            while (reader.HasMore)
            {
                int field;
                int wire;
                reader.ReadTag(out field, out wire);

                if (field == 1 && wire == 2)
                {
                    cursor.Type = reader.ReadString();
                }
                else if (field == 2 && wire == 2)
                {
                    ProtoReader inner = new ProtoReader(reader.ReadBytes());
                    while (inner.HasMore)
                    {
                        int innerField;
                        int innerWire;
                        inner.ReadTag(out innerField, out innerWire);

                        if (innerField == 1 && innerWire == 2)
                            cursor.ImageData = inner.ReadBytes();
                        else if (innerField == 2 && innerWire == 0)
                            cursor.ImageFormat = (int)inner.ReadVarint();
                        else if (innerField == 3 && innerWire == 2)
                            cursor.Hotspot = ParseHotspot(inner.ReadBytes());
                        else if (innerField == 6 && innerWire == 2)
                            cursor.SvgLayers.AddRange(ParseSvgLayers(inner.ReadBytes()));
                        else
                            inner.Skip(innerWire);
                    }
                }
                else
                {
                    reader.Skip(wire);
                }
            }

            return cursor;
        }

        static List<byte[]> ParseSvgLayers(byte[] data)
        {
            List<byte[]> layers = new List<byte[]>();
            ProtoReader reader = new ProtoReader(data);

            while (reader.HasMore)
            {
                int field;
                int wire;
                reader.ReadTag(out field, out wire);

                if (field == 1 && wire == 2)
                {
                    byte[] svg = ParseLayerSvg(reader.ReadBytes());
                    if (svg != null && svg.Length > 0)
                        layers.Add(svg);
                }
                else
                {
                    reader.Skip(wire);
                }
            }

            return layers;
        }

        static byte[] ParseLayerSvg(byte[] data)
        {
            ProtoReader reader = new ProtoReader(data);

            while (reader.HasMore)
            {
                int field;
                int wire;
                reader.ReadTag(out field, out wire);

                if (field == 2 && wire == 2)
                {
                    ProtoReader image = new ProtoReader(reader.ReadBytes());
                    while (image.HasMore)
                    {
                        int imageField;
                        int imageWire;
                        image.ReadTag(out imageField, out imageWire);
                        if (imageField == 1 && imageWire == 2)
                            return image.ReadBytes();
                        image.Skip(imageWire);
                    }
                }
                else
                {
                    reader.Skip(wire);
                }
            }

            return null;
        }

        static float[] ParseHotspot(byte[] data)
        {
            List<float> values = new List<float>();
            ProtoReader reader = new ProtoReader(data);

            while (reader.HasMore)
            {
                int field;
                int wire;
                reader.ReadTag(out field, out wire);
                if (wire == 5)
                    values.Add(reader.ReadFloat());
                else
                    reader.Skip(wire);
            }

            return values.ToArray();
        }

        public static string NormalizeType(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "UNKNOWN";

            string type = value.Trim().ToUpperInvariant().Replace('-', '_').Replace(' ', '_');
            switch (type)
            {
                case "NORMAL":
                case "POINTER":
                    return "ARROW";
                case "TEXT":
                case "I_BEAM":
                    return "IBEAM";
                case "CROSSHAIR":
                    return "CROSS";
                case "NO":
                case "UNAVAILABLE":
                    return "FORBIDDEN";
                case "HAND":
                case "POINTINGHAND":
                case "LINK":
                    return "POINTING_HAND";
                case "HELP":
                    return "WHATSTHIS";
                case "SIZE_NS":
                case "NS":
                    return "SIZE_VERTICAL";
                case "SIZE_WE":
                case "WE":
                    return "SIZE_HORIZONTAL";
                case "SIZE_NWSE":
                case "NWSE":
                    return "SIZE_BDIAGONAL";
                case "SIZE_NESW":
                case "NESW":
                    return "SIZE_FDIAGONAL";
                case "MOVE":
                case "ALL":
                    return "SIZE_ALL";
                case "UP":
                case "UPARROW":
                case "ALTERNATE":
                    return "UPARROW";
                default:
                    return type;
            }
        }
    }
}
