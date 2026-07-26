using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace PsobbLauncher
{
    /// <summary>
    /// Reads and patches the <c>Adapters</c> key in dgVoodoo2's dgVoodoo.conf.
    ///
    /// Why this exists: dgVoodoo presents a SINGLE virtual D3D8 adapter to PSOBB
    /// no matter how many GPUs are fitted, so the game cannot choose a card and
    /// neither can the client. The choice belongs to the wrapper, and it lives in
    /// dgVoodoo.conf under [General] as:
    ///
    ///     Adapters                             = 1
    ///
    /// Values are dgVoodoo's own host-adapter ordinals and are 1-BASED, or the
    /// literal "all" to let dgVoodoo pick. Confirmed empirically by generating one
    /// conf per card with dgVoodooCpl.exe and diffing: that single line was the
    /// only difference.
    ///
    /// Deliberately PATCHES rather than rewrites. A user's dgVoodoo.conf may carry
    /// settings they have tuned by hand, and clobbering it would be hostile. Same
    /// approach the upstream widescreen repo takes in install_dgvoodoo.ps1.
    /// </summary>
    public static class DgVoodooConfig
    {
        public const string FileName = "dgVoodoo.conf";
        public const string AutoValue = "all";

        // Matches the whole line, capturing the key plus its column padding so the
        // file's alignment survives a rewrite.
        private static readonly Regex AdaptersLine = new Regex(
            @"(?m)^(?<lead>[ \t]*Adapters[ \t]*=[ \t]*)(?<val>[^\r\n;]*)",
            RegexOptions.Compiled);

        public static string PathFor(string gameDir) =>
            Path.Combine(gameDir, FileName);

        public static bool Exists(string gameDir) =>
            File.Exists(PathFor(gameDir));

        /// <summary>
        /// Current value of <c>Adapters</c>, trimmed, or null if the file or key is
        /// absent. Returns raw text - compare against <see cref="AutoValue"/>
        /// rather than assuming it parses as a number.
        /// </summary>
        public static string ReadAdapters(string gameDir)
        {
            string path = PathFor(gameDir);
            if (!File.Exists(path)) return null;

            Match m = AdaptersLine.Match(File.ReadAllText(path));
            return m.Success ? m.Groups["val"].Value.Trim() : null;
        }

        /// <summary>
        /// Parsed 1-based ordinal, or null when the value is "all", missing, or not
        /// a number we recognise.
        /// </summary>
        public static int? ReadAdapterOrdinal(string gameDir)
        {
            string raw = ReadAdapters(gameDir);
            if (string.IsNullOrEmpty(raw)) return null;
            return int.TryParse(raw, out int n) && n >= 1 ? n : (int?)null;
        }

        /// <summary>
        /// Writes <paramref name="value"/> to the <c>Adapters</c> key. Pass
        /// <see cref="AutoValue"/> for automatic selection, or a 1-based ordinal.
        ///
        /// Returns false if the file does not exist or has no Adapters key - the
        /// caller should surface that rather than pretend it worked, because
        /// dgVoodoo silently falls back to defaults when the file is absent.
        /// Makes a one-time backup and only writes when the content changes.
        /// </summary>
        public static bool WriteAdapters(string gameDir, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;

            string path = PathFor(gameDir);
            if (!File.Exists(path)) return false;

            string original = File.ReadAllText(path);
            Match m = AdaptersLine.Match(original);
            if (!m.Success) return false;

            if (m.Groups["val"].Value.Trim().Equals(value.Trim(),
                    StringComparison.OrdinalIgnoreCase))
                return true; // already correct, leave the file alone

            string updated = AdaptersLine.Replace(original,
                mm => mm.Groups["lead"].Value + value.Trim(), 1);

            string backup = path + ".launcher-bak";
            if (!File.Exists(backup)) File.Copy(path, backup);

            // dgVoodoo.conf is CRLF throughout; keep it that way.
            updated = Regex.Replace(updated, "\r?\n", "\r\n");
            File.WriteAllText(path, updated, new UTF8Encoding(false));
            return true;
        }

        public static bool SetAutomatic(string gameDir) =>
            WriteAdapters(gameDir, AutoValue);

        public static bool SetAdapterOrdinal(string gameDir, int oneBasedOrdinal)
        {
            if (oneBasedOrdinal < 1) return false;
            return WriteAdapters(gameDir, oneBasedOrdinal.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}
