using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace CurskinInstaller
{
    class InstallResult
    {
        public List<string> SchemeNames = new List<string>();
        public string MixedScheme;
        public string AppliedScheme;
        public string InstallDir = "";
        public int FileCount;
        public bool MouseXRunning;
    }

    class VariantInstall
    {
        public CurskinVariant Variant;
        public string SchemeName;
        public Dictionary<string, string> Files =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    static class Installer
    {
        public static readonly string[] Roles =
        {
            "Arrow", "Help", "AppStarting", "Wait", "Crosshair", "IBeam", "NWPen",
            "No", "SizeNS", "SizeWE", "SizeNWSE", "SizeNESW", "SizeAll", "UpArrow",
            "Hand", "Person", "Pin"
        };

        // The Schemes registry value is positional, and Windows expects Location
        // Select (Pin) before Person Select (Person). The named values under
        // Control Panel\Cursors are still Person and Pin, so Roles stays in the
        // order used by ApplyScheme and the fallback table.
        private static readonly string[] SchemeOrder =
        {
            "Arrow", "Help", "AppStarting", "Wait", "Crosshair", "IBeam", "NWPen",
            "No", "SizeNS", "SizeWE", "SizeNWSE", "SizeNESW", "SizeAll", "UpArrow",
            "Hand", "Pin", "Person"
        };

        private static readonly string[][] Fallbacks =
        {
            new string[] { "ARROW" },
            new string[] { "WHATSTHIS", "ARROW" },
            new string[] { "BUSY", "WAIT", "ARROW" },
            new string[] { "WAIT", "BUSY", "ARROW" },
            new string[] { "CROSS", "ARROW" },
            new string[] { "IBEAM", "ARROW" },
            new string[] { "POINTING_HAND", "IBEAM", "ARROW" },
            new string[] { "FORBIDDEN", "CROSS", "ARROW" },
            new string[] { "SIZE_VERTICAL", "SIZE_ALL", "ARROW" },
            new string[] { "SIZE_HORIZONTAL", "SIZE_ALL", "ARROW" },
            new string[] { "SIZE_BDIAGONAL", "SIZE_ALL", "ARROW" },
            new string[] { "SIZE_FDIAGONAL", "SIZE_ALL", "ARROW" },
            new string[] { "SIZE_ALL", "ARROW" },
            new string[] { "ARROW" },
            new string[] { "POINTING_HAND", "ARROW" },
            new string[] { "PERSON", "POINTING_HAND", "ARROW" },
            new string[] { "PIN", "CROSS", "ARROW" }
        };

        private const string CursorKeyPath = @"Control Panel\Cursors";
        private const string SchemesKeyPath = @"Control Panel\Cursors\Schemes";
        private const string BackupFileName = "original-cursors.txt";

        public static InstallResult Install(string sourcePath, CurskinSkin skin,
            List<CurskinVariant> variants, bool createMixed, bool apply, Action<string> progress)
        {
            if (skin == null)
                throw new ArgumentNullException("skin");
            if (variants == null || variants.Count == 0)
                throw new InvalidOperationException("Select at least one variant.");

            string skinName = string.IsNullOrWhiteSpace(skin.Name)
                ? CurskinParser.FallbackName(sourcePath)
                : skin.Name.Trim();
            if (string.IsNullOrWhiteSpace(skinName))
                skinName = "Curskin";

            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft");
            folder = Path.Combine(folder, "Windows");
            folder = Path.Combine(folder, "Cursors");
            folder = Path.Combine(folder, SafeFileName(skinName, "Curskin"));
            Directory.CreateDirectory(folder);

            InstallResult result = new InstallResult();
            result.InstallDir = folder;

            List<VariantInstall> installed = new List<VariantInstall>();
            HashSet<string> usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int variantNumber = 0;

            foreach (CurskinVariant variant in variants)
            {
                variantNumber++;
                if (variant == null || variant.Cursors.Count == 0)
                    continue;

                string variantName = string.IsNullOrWhiteSpace(variant.Name)
                    ? "Variant " + variantNumber.ToString()
                    : variant.Name.Trim();
                string schemeName = MakeUniqueName(skinName + " - " + variantName, usedNames);

                VariantInstall info = new VariantInstall();
                info.Variant = variant;
                info.SchemeName = schemeName;
                string fileBase = Slug(variantName) + "_" + variantNumber.ToString();

                foreach (string role in Roles)
                {
                    CursorDef cursor = FindCursor(variant, role);
                    if (cursor == null)
                        throw new InvalidOperationException("No cursor is available for " + role + " in " + variantName + ".");

                    if (progress != null)
                        progress("Converting " + variantName + " / " + role + "...");

                    BuiltCursor built;
                    try
                    {
                        built = CursorBuilder.Build(cursor,
                            fileBase + "_" + role.ToLowerInvariant());
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException("Could not convert " + variantName + " / " + role + ": " + ex.Message, ex);
                    }

                    string file = Path.Combine(folder,
                        fileBase + "_" + role.ToLowerInvariant() + built.Extension);
                    File.WriteAllBytes(file, built.Data);
                    info.Files[role] = file;
                    result.FileCount++;
                }

                installed.Add(info);
                result.SchemeNames.Add(schemeName);
            }

            if (installed.Count == 0)
                throw new InvalidOperationException("No usable cursors were found in this .curskin file.");

            if (createMixed && installed.Count >= 2)
            {
                string mixedName = MakeUniqueName(skinName + " - Mixed", usedNames);
                VariantInstall mixed = new VariantInstall();
                mixed.SchemeName = mixedName;
                for (int i = 0; i < Roles.Length; i++)
                {
                    VariantInstall source = installed[i % installed.Count];
                    mixed.Files[Roles[i]] = source.Files[Roles[i]];
                }
                installed.Add(mixed);
                result.SchemeNames.Add(mixedName);
                result.MixedScheme = mixedName;
            }

            foreach (VariantInstall info in installed)
            {
                RegisterScheme(info.SchemeName, info.Files);
                if (progress != null)
                    progress("Registered " + info.SchemeName);
            }

            result.MouseXRunning = IsMouseXRunning();

            if (apply)
            {
                VariantInstall chosen = null;
                if (result.MixedScheme != null)
                {
                    foreach (VariantInstall info in installed)
                    {
                        if (string.Equals(info.SchemeName, result.MixedScheme, StringComparison.OrdinalIgnoreCase))
                        {
                            chosen = info;
                            break;
                        }
                    }
                }
                if (chosen == null)
                    chosen = installed[0];

                try
                {
                    BackupOriginalCursors();
                    ApplyScheme(chosen.SchemeName, chosen.Files);
                    result.AppliedScheme = chosen.SchemeName;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        "The schemes were installed, but applying the scheme failed: " + ex.Message, ex);
                }
            }

            return result;
        }

        private static CursorDef FindCursor(CurskinVariant variant, string role)
        {
            int index = Array.IndexOf(Roles, role);
            if (index >= 0)
            {
                string[] chain = Fallbacks[index];
                foreach (string type in chain)
                {
                    CursorDef cursor;
                    if (variant.Cursors.TryGetValue(type, out cursor))
                        return cursor;
                }
            }

            foreach (KeyValuePair<string, CursorDef> pair in variant.Cursors)
                return pair.Value;
            return null;
        }

        private static void RegisterScheme(string name, Dictionary<string, string> files)
        {
            string[] paths = new string[SchemeOrder.Length];
            for (int i = 0; i < SchemeOrder.Length; i++)
            {
                string role = SchemeOrder[i];
                string path;
                if (!files.TryGetValue(role, out path) || string.IsNullOrEmpty(path))
                    throw new InvalidOperationException("The scheme is missing a path for " + role + ".");
                paths[i] = path;
            }

            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(SchemesKeyPath))
            {
                key.SetValue(name, string.Join(",", paths), RegistryValueKind.ExpandString);
            }
        }

        private static void ApplyScheme(string name, Dictionary<string, string> files)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(CursorKeyPath))
            {
                foreach (string role in Roles)
                {
                    string path;
                    if (!files.TryGetValue(role, out path))
                        throw new InvalidOperationException("The scheme is missing " + role + ".");
                    key.SetValue(role, path, RegistryValueKind.ExpandString);
                }
                key.SetValue("", name, RegistryValueKind.String);
                key.SetValue("Scheme Source", 1, RegistryValueKind.DWord);
            }

            NativeMethods.SystemParametersInfo(NativeMethods.SPI_SETCURSORS, 0, IntPtr.Zero,
                NativeMethods.SPIF_SENDCHANGE);
        }

        public static bool HasBackup
        {
            get { return File.Exists(GetBackupPath()); }
        }

        private static string GetBackupPath()
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CurskinInstaller");
            return Path.Combine(folder, BackupFileName);
        }

        private static void BackupOriginalCursors()
        {
            string path = GetBackupPath();
            if (File.Exists(path))
                return;

            string folder = Path.GetDirectoryName(path);
            Directory.CreateDirectory(folder);

            List<string> lines = new List<string>();
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(CursorKeyPath))
            {
                lines.Add("Default=" + ReadString(key, ""));
                lines.Add("SchemeSource=" + ReadString(key, "Scheme Source"));
                foreach (string role in Roles)
                    lines.Add(role + "=" + ReadString(key, role));
            }

            File.WriteAllLines(path, lines.ToArray(), Encoding.UTF8);
        }

        private static string ReadString(RegistryKey key, string name)
        {
            if (key == null)
                return "";
            object value = key.GetValue(name);
            if (value == null)
                return "";
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        public static string RestorePrevious()
        {
            string path = GetBackupPath();
            if (!File.Exists(path))
                throw new InvalidOperationException("There is no saved cursor backup yet.");

            Dictionary<string, string> values =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in File.ReadAllLines(path))
            {
                int equals = line.IndexOf('=');
                if (equals < 0)
                    continue;
                values[line.Substring(0, equals)] = line.Substring(equals + 1);
            }

            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(CursorKeyPath))
            {
                foreach (string role in Roles)
                    RestoreStringValue(key, role, role, values);
                RestoreStringValue(key, "", "Default", values);

                string schemeSource;
                int source;
                if (values.TryGetValue("SchemeSource", out schemeSource) &&
                    int.TryParse(schemeSource, NumberStyles.Integer, CultureInfo.InvariantCulture, out source))
                {
                    key.SetValue("Scheme Source", source, RegistryValueKind.DWord);
                }
                else
                {
                    key.DeleteValue("Scheme Source", false);
                }
            }

            NativeMethods.SystemParametersInfo(NativeMethods.SPI_SETCURSORS, 0, IntPtr.Zero,
                NativeMethods.SPIF_SENDCHANGE);
            return "The cursor settings saved before the first install were restored.";
        }

        private static void RestoreStringValue(RegistryKey key, string registryName,
            string backupName, Dictionary<string, string> values)
        {
            string value;
            if (values.TryGetValue(backupName, out value) && !string.IsNullOrEmpty(value))
                key.SetValue(registryName, value, RegistryValueKind.ExpandString);
            else
                key.DeleteValue(registryName, false);
        }

        public static bool IsMouseXRunning()
        {
            try
            {
                return Process.GetProcessesByName("cursorqt_core").Length > 0;
            }
            catch
            {
                return false;
            }
        }

        public static void OpenMouseProperties()
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo("control.exe", "mouse");
                info.UseShellExecute = true;
                Process.Start(info);
            }
            catch
            {
            }
        }

        public static string SafeFileName(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
                value = fallback;

            char[] invalid = Path.GetInvalidFileNameChars();
            StringBuilder result = new StringBuilder();
            foreach (char c in value.Trim())
            {
                if (Array.IndexOf(invalid, c) >= 0)
                    continue;
                if (c == ',' || c == ';')
                    continue;
                result.Append(c);
            }

            string cleaned = result.ToString().Trim().TrimEnd('.');
            if (cleaned.Length > 60)
                cleaned = cleaned.Substring(0, 60).TrimEnd();
            if (cleaned.Length == 0)
                cleaned = fallback;
            return cleaned;
        }

        public static string Slug(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "cursor";

            StringBuilder result = new StringBuilder();
            bool lastUnderscore = false;
            foreach (char c in value.ToLowerInvariant())
            {
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                {
                    result.Append(c);
                    lastUnderscore = false;
                }
                else if (!lastUnderscore)
                {
                    result.Append('_');
                    lastUnderscore = true;
                }
            }

            string slug = result.ToString().Trim('_');
            if (slug.Length == 0)
                slug = "cursor";
            if (slug.Length > 40)
                slug = slug.Substring(0, 40).Trim('_');
            if (slug.Length == 0)
                slug = "cursor";
            return slug;
        }

        private static string MakeUniqueName(string name, HashSet<string> used)
        {
            name = name == null ? "" : name.Trim();
            if (name.Length == 0)
                name = "Cursors";
            if (name.Length > 200)
                name = name.Substring(0, 200).Trim();

            string baseName = name;
            int suffix = 2;
            while (used.Contains(name))
            {
                name = baseName + " (" + suffix.ToString() + ")";
                suffix++;
            }
            used.Add(name);
            return name;
        }
    }

    static class NativeMethods
    {
        public const int SPI_SETCURSORS = 0x0057;
        public const int SPIF_SENDCHANGE = 0x02;

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        public static extern bool SystemParametersInfo(int action, int param, IntPtr data, int winIni);
    }
}
