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
        public bool StartupRegistered;
        public string StartupError;
        public bool ThemeSynced;
        public string ThemeFile = "";
        public string ThemeError;
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

        // Role order used inside a .theme file. Windows writes the section in
        // alphabetical order, so keeping that order makes the file look exactly
        // like the one Personalization writes.
        private static readonly string[] ThemeRoleOrder =
        {
            "AppStarting", "Arrow", "Crosshair", "Hand", "Help", "IBeam", "No",
            "NWPen", "Person", "Pin", "SizeAll", "SizeNESW", "SizeNS", "SizeNWSE",
            "SizeWE", "UpArrow", "Wait"
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
        private const string ThemesKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes";
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "CurskinInstaller";
        private const string BackupFileName = "original-cursors.txt";
        private const string ThemeBackupFileName = "original-theme.txt";
        private const string CursorSectionHeader = "[Control Panel\\Cursors]";

        // System cursor slots accepted by SetSystemCursor. The three roles with
        // an id of 0 have no slot; SPI_SETCURSORS is the only way to set them.
        private static readonly uint[] CursorSlotIds =
        {
            32512, // Arrow      OCR_NORMAL
            32651, // Help       OCR_HELP
            32650, // AppStarting OCR_APPSTARTING
            32514, // Wait       OCR_WAIT
            32515, // Crosshair  OCR_CROSS
            32513, // IBeam      OCR_IBEAM
            0,     // NWPen      (no system cursor slot)
            32648, // No         OCR_NO
            32645, // SizeNS     OCR_SIZENS
            32644, // SizeWE     OCR_SIZEWE
            32642, // SizeNWSE   OCR_SIZENWSE
            32643, // SizeNESW   OCR_SIZENESW
            32646, // SizeAll    OCR_SIZEALL
            32516, // UpArrow    OCR_UP
            32649, // Hand       OCR_HAND
            0,     // Person     (no system cursor slot)
            0      // Pin        (no system cursor slot)
        };

        public static InstallResult Install(string sourcePath, CurskinSkin skin,
            List<CurskinVariant> variants, bool createMixed, bool apply, bool keepAfterRestart,
            Action<string> progress)
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

                // Keep the active theme file in sync with the scheme. Windows
                // reloads that file during logon, so leaving it alone is what
                // makes the cursors revert after a restart.
                SyncActiveTheme(chosen.SchemeName, chosen.Files, result);
            }

            try
            {
                if (apply && keepAfterRestart)
                {
                    RegisterStartup();
                    result.StartupRegistered = true;
                }
                else if (apply)
                {
                    UnregisterStartup();
                }
            }
            catch (Exception ex)
            {
                // The cursors are already applied; a missing sign-in entry
                // only means Windows may show its own cursors after logon.
                result.StartupError = ex.Message;
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

        // ---- Active theme file ------------------------------------------------

        // A .theme file carries its own [Control Panel\Cursors] section, and
        // Windows reloads the active theme during logon. If that section still
        // points at the default cursors, the scheme stays selected in the
        // registry but the session comes up with Windows' own pointers. Keeping
        // the file in sync makes a restart harmless without a startup task.
        private static void SyncActiveTheme(string schemeName, Dictionary<string, string> files,
            InstallResult result)
        {
            string themePath = GetActiveThemePath();
            if (themePath == null || !File.Exists(themePath))
                return;

            try
            {
                Encoding encoding;
                List<string> lines = ReadThemeFile(themePath, out encoding);

                int start;
                int end;
                FindCursorSection(lines, out start, out end);
                if (start < 0)
                {
                    // This theme does not own the pointers, so a logon reload
                    // leaves the registry values alone.
                    return;
                }

                BackupThemeSection(themePath, encoding, lines, start, end);

                List<string> section = new List<string>();
                foreach (string role in ThemeRoleOrder)
                {
                    string path;
                    if (files.TryGetValue(role, out path) && !string.IsNullOrEmpty(path))
                        section.Add(role + "=" + path);
                }
                section.Add("DefaultValue=" + schemeName);

                // Keep any other lines that already lived in the section.
                for (int i = start + 1; i < end; i++)
                {
                    string key = ThemeKey(lines[i]);
                    if (key == null)
                    {
                        if (lines[i].Trim().Length > 0)
                            section.Add(lines[i]);
                        continue;
                    }
                    if (IsThemeRole(key) ||
                        string.Equals(key, "DefaultValue", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    section.Add(lines[i]);
                }

                lines.RemoveRange(start + 1, end - start - 1);
                lines.InsertRange(start + 1, section);

                WriteThemeFile(themePath, lines, encoding);
                result.ThemeFile = themePath;
                result.ThemeSynced = true;
            }
            catch (Exception ex)
            {
                // The cursors are already applied, so a read-only or missing
                // theme file is only worth a warning.
                result.ThemeError = ex.Message;
            }
        }

        private static string GetActiveThemePath()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(ThemesKeyPath))
                {
                    if (key == null)
                        return null;

                    string path = ReadString(key, "CurrentTheme");
                    if (string.IsNullOrWhiteSpace(path))
                        return null;

                    path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
                    return path.EndsWith(".theme", StringComparison.OrdinalIgnoreCase) ? path : null;
                }
            }
            catch
            {
                return null;
            }
        }

        private static bool IsThemeRole(string key)
        {
            foreach (string role in Roles)
            {
                if (string.Equals(role, key, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static void FindCursorSection(List<string> lines, out int start, out int end)
        {
            start = -1;
            end = lines.Count;

            for (int i = 0; i < lines.Count; i++)
            {
                string trimmed = lines[i].Trim();
                if (trimmed.Length == 0 || trimmed[0] != '[')
                    continue;

                if (start >= 0)
                {
                    end = i;
                    break;
                }

                if (string.Equals(trimmed, CursorSectionHeader, StringComparison.OrdinalIgnoreCase))
                    start = i;
            }
        }

        private static string ThemeKey(string line)
        {
            int equals = line.IndexOf('=');
            if (equals <= 0)
                return null;

            string key = line.Substring(0, equals).Trim();
            if (key.Length == 0)
                return null;

            foreach (char c in key)
            {
                if (c == '[' || c == ';')
                    return null;
            }
            return key;
        }

        private static List<string> ReadThemeFile(string path, out Encoding encoding)
        {
            byte[] raw = File.ReadAllBytes(path);
            int offset = 0;

            if (raw.Length >= 2 && raw[0] == 0xFF && raw[1] == 0xFE)
            {
                encoding = Encoding.Unicode;
                offset = 2;
            }
            else if (raw.Length >= 2 && raw[0] == 0xFE && raw[1] == 0xFF)
            {
                encoding = Encoding.BigEndianUnicode;
                offset = 2;
            }
            else if (raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF)
            {
                encoding = new UTF8Encoding(true);
                offset = 3;
            }
            else
            {
                encoding = new UTF8Encoding(false);
            }

            string text = encoding.GetString(raw, offset, raw.Length - offset)
                .Replace("\r\n", "\n").Replace('\r', '\n');

            List<string> lines = new List<string>(text.Split('\n'));
            if (lines.Count > 0 && lines[lines.Count - 1].Length == 0)
                lines.RemoveAt(lines.Count - 1);
            return lines;
        }

        private static void WriteThemeFile(string path, List<string> lines, Encoding encoding)
        {
            File.WriteAllText(path, string.Join("\r\n", lines.ToArray()) + "\r\n", encoding);
        }

        private static Encoding ParseThemeEncoding(string name, bool bom)
        {
            if (string.Equals(name, "utf-16", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "utf-16le", StringComparison.OrdinalIgnoreCase))
            {
                return Encoding.Unicode;
            }
            if (string.Equals(name, "utf-16be", StringComparison.OrdinalIgnoreCase))
                return Encoding.BigEndianUnicode;
            return new UTF8Encoding(bom);
        }

        private static string GetThemeBackupPath()
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CurskinInstaller");
            return Path.Combine(folder, ThemeBackupFileName);
        }

        private static void BackupThemeSection(string themePath, Encoding encoding,
            List<string> lines, int start, int end)
        {
            string path = GetThemeBackupPath();
            if (File.Exists(path))
                return;

            Directory.CreateDirectory(Path.GetDirectoryName(path));

            List<string> backup = new List<string>();
            backup.Add("Path=" + themePath);
            backup.Add("Encoding=" + encoding.WebName);
            backup.Add("Bom=" + (encoding.GetPreamble().Length > 0 ? "1" : "0"));
            backup.Add("#cursors");
            for (int i = start + 1; i < end; i++)
                backup.Add(lines[i]);

            File.WriteAllLines(path, backup.ToArray(), Encoding.UTF8);
        }

        private static void RestoreThemeSection()
        {
            string path = GetThemeBackupPath();
            if (!File.Exists(path))
                return;

            string themePath = "";
            string encodingName = "utf-8";
            bool bom = false;
            bool inSection = false;
            List<string> section = new List<string>();

            foreach (string line in File.ReadAllLines(path))
            {
                if (!inSection)
                {
                    if (line.StartsWith("Path=", StringComparison.Ordinal))
                        themePath = line.Substring(5);
                    else if (line.StartsWith("Encoding=", StringComparison.Ordinal))
                        encodingName = line.Substring(9);
                    else if (line.StartsWith("Bom=", StringComparison.Ordinal))
                        bom = line.Substring(4) == "1";
                    else if (line == "#cursors")
                        inSection = true;
                    continue;
                }
                section.Add(line);
            }

            try
            {
                if (themePath.Length > 0 && File.Exists(themePath))
                {
                    Encoding readEncoding;
                    List<string> lines = ReadThemeFile(themePath, out readEncoding);

                    int start;
                    int end;
                    FindCursorSection(lines, out start, out end);
                    if (start >= 0)
                    {
                        lines.RemoveRange(start + 1, end - start - 1);
                        lines.InsertRange(start + 1, section);
                        WriteThemeFile(themePath, lines, ParseThemeEncoding(encodingName, bom));
                    }
                }
            }
            catch
            {
                // A missing or locked theme file must not block the restore.
            }

            try
            {
                File.Delete(path);
            }
            catch
            {
            }
        }

        public static bool StartupRegistered
        {
            get
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath))
                    {
                        return key != null && key.GetValue(RunValueName) != null;
                    }
                }
                catch
                {
                    return false;
                }
            }
        }

        public static void RegisterStartup()
        {
            string exe = Process.GetCurrentProcess().MainModule.FileName;
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
                throw new InvalidOperationException("Could not find the Curskin Installer executable.");

            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
            {
                key.SetValue(RunValueName, "\"" + exe + "\" --reapply", RegistryValueKind.String);
            }
        }

        public static void UnregisterStartup()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
            {
                if (key != null)
                    key.DeleteValue(RunValueName, false);
            }
        }

        // Windows sometimes restores its own cursors at sign-in even though the
        // custom scheme is still selected in the registry (fast startup, theme
        // handling, and recent Windows 11 builds are known to do this). Re-apply
        // the current HKCU selection to the logged-on session.
        public static int ReapplyCurrentScheme()
        {
            string[] paths = new string[Roles.Length];

            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(CursorKeyPath))
            {
                if (key == null)
                    return 0;

                for (int i = 0; i < Roles.Length; i++)
                {
                    object value = key.GetValue(Roles[i]);
                    if (value == null)
                        continue;

                    string path = Convert.ToString(value, CultureInfo.InvariantCulture);
                    if (string.IsNullOrWhiteSpace(path))
                        continue;

                    paths[i] = Environment.ExpandEnvironmentVariables(path.Trim());
                }
            }

            // Reload every role from the registry first. This is the only API
            // that also applies NWPen, Person, and Pin.
            NativeMethods.SystemParametersInfo(NativeMethods.SPI_SETCURSORS, 0, IntPtr.Zero,
                NativeMethods.SPIF_SENDCHANGE);

            // Then force the roles that have a system cursor slot. This covers
            // systems where the registry reload silently keeps its own cursors.
            int applied = 0;
            for (int i = 0; i < Roles.Length; i++)
            {
                uint slot = CursorSlotIds[i];
                string path = paths[i];
                if (slot == 0 || string.IsNullOrEmpty(path) || !File.Exists(path))
                    continue;

                IntPtr handle = NativeMethods.LoadCursorFromFile(path);
                if (handle == IntPtr.Zero)
                    continue;

                // SetSystemCursor takes ownership of the handle and destroys it.
                if (NativeMethods.SetSystemCursor(handle, slot))
                    applied++;
            }

            return applied;
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

            bool hadStartup = StartupRegistered;

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

            bool hadTheme = File.Exists(GetThemeBackupPath());
            RestoreThemeSection();

            UnregisterStartup();

            string message = "The cursor settings saved before the first install were restored.";
            if (hadTheme)
                message += " The active Windows theme was restored as well.";
            if (hadStartup)
                message += " Automatic re-applying at sign-in was turned off.";
            return message;
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

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true,
            CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        public static extern IntPtr LoadCursorFromFile(string fileName);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetSystemCursor(IntPtr cursor, uint id);
    }
}
