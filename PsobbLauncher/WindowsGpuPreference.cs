using System;
using System.IO;
using System.Runtime.InteropServices;

namespace PsobbLauncher
{
    /// <summary>
    /// Windows' own per-application GPU preference - the same setting as
    /// Settings > System > Display > Graphics.
    ///
    /// Stored at HKCU\Software\Microsoft\DirectX\UserGpuPreferences as a REG_SZ
    /// per executable: the value NAME is the full exe path, the DATA looks like
    ///     GpuPreference=2;
    /// with 0 = let Windows decide, 1 = power saving (usually the iGPU),
    /// 2 = high performance (usually the discrete card). The trailing semicolon
    /// matters. Other settings such as AutoHDREnable share the same string.
    /// </summary>
    public static class WindowsGpuPreference
    {
        public const int LetWindowsDecide = 0;
        public const int PowerSaving      = 1;
        public const int HighPerformance  = 2;

        private const string RegPath =
            @"Software\Microsoft\DirectX\UserGpuPreferences";

        private static bool OnWindows =>
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        /// <summary>
        /// Current preference for an executable, or null if none is set.
        /// Parses the GpuPreference token out of the combined string rather than
        /// assuming it is the only one present.
        /// </summary>
        public static int? Read(string exePath)
        {
            if (!OnWindows || string.IsNullOrWhiteSpace(exePath)) return null;

            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RegPath);
            string raw = key?.GetValue(Path.GetFullPath(exePath)) as string;
            if (string.IsNullOrEmpty(raw)) return null;

            foreach (string part in raw.Split(';'))
            {
                string p = part.Trim();
                if (!p.StartsWith("GpuPreference=", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (int.TryParse(p.Substring("GpuPreference=".Length), out int v))
                    return v;
            }
            return null;
        }

        /// <summary>
        /// Sets the preference for an executable. Passing
        /// <see cref="LetWindowsDecide"/> REMOVES the entry, which is what the
        /// Windows UI does for "Let Windows decide" - writing GpuPreference=0
        /// leaves a redundant entry behind.
        ///
        /// Preserves any other tokens already in the string (AutoHDREnable and
        /// friends) instead of overwriting the whole value.
        /// </summary>
        public static bool Write(string exePath, int preference)
        {
            if (!OnWindows || string.IsNullOrWhiteSpace(exePath)) return false;
            if (preference < 0 || preference > 2) return false;

            string full = Path.GetFullPath(exePath);
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RegPath);
            if (key == null) return false;

            string existing = key.GetValue(full) as string ?? "";
            var kept = new System.Collections.Generic.List<string>();
            foreach (string part in existing.Split(';'))
            {
                string p = part.Trim();
                if (p.Length == 0) continue;
                if (p.StartsWith("GpuPreference=", StringComparison.OrdinalIgnoreCase))
                    continue;
                kept.Add(p);
            }

            if (preference == LetWindowsDecide && kept.Count == 0)
            {
                // Nothing left to say about this exe - drop the entry entirely,
                // matching what the Windows UI does.
                if (key.GetValue(full) != null) key.DeleteValue(full, false);
                return true;
            }

            if (preference != LetWindowsDecide)
                kept.Add("GpuPreference=" + preference.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));

            key.SetValue(full, string.Join(";", kept) + ";",
                Microsoft.Win32.RegistryValueKind.String);
            return true;
        }

        public static bool Clear(string exePath) =>
            Write(exePath, LetWindowsDecide);
    }
}
