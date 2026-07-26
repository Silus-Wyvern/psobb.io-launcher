using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace PsobbLauncher
{
    /// <summary>One display adapter as Windows reports it.</summary>
    public sealed class GpuAdapter
    {
        /// <summary>Human-readable name, e.g. "AMD Radeon RX 7600 XT".</summary>
        public string Name { get; set; }

        /// <summary>Windows device key (\\.\DISPLAY1). Not stable; do not persist.</summary>
        public string DeviceName { get; set; }

        /// <summary>True when this adapter drives the primary display.</summary>
        public bool IsPrimary { get; set; }

        /// <summary>1-based dgVoodoo ordinal, primary first. Recomputed per launch.</summary>
        public int DgVoodooOrdinal { get; set; }

        public override string ToString() =>
            IsPrimary ? Name + " (primary display)" : Name;
    }

    /// <summary>
    /// Enumerates display adapters that currently have a display attached.
    ///
    /// Why only attached ones: dgVoodoo can only create a device on an adapter it
    /// can present to. Selecting a headless card CRASHES the client outright - it
    /// does not validate or fall back. Verified: ordinal 2 crashed repeatedly
    /// until a dummy plug was fitted to that card, after which it worked.
    /// </summary>
    public static class GpuAdapters
    {
        // NOTE ON ORDINAL STABILITY: dgVoodoo's ordinals follow DISPLAY ORDER,
        // not the physical card. Moving a monitor to another port, or changing
        // which display is primary, renumbers them - verified by swapping the
        // cable, after which ordinals 1 and 2 swapped cards. So persist the
        // adapter NAME and resolve its ordinal at launch. Never store the number.

        private const int ATTACHED_TO_DESKTOP = 0x00000001;
        private const int PRIMARY_DEVICE      = 0x00000004;
        private const int MIRRORING_DRIVER    = 0x00000008;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceKey;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplayDevices(
            string lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice,
            uint dwFlags);

        /// <summary>
        /// Adapters with a display attached, ordered as dgVoodoo numbers them:
        /// primary first, then the rest in enumeration order. DgVoodooOrdinal is
        /// filled in 1-based. Returns an empty list on non-Windows.
        /// </summary>
        public static List<GpuAdapter> Enumerate()
        {
            var found = new List<GpuAdapter>();
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return found;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (uint i = 0; ; i++)
            {
                var dd = new DISPLAY_DEVICE();
                dd.cb = Marshal.SizeOf<DISPLAY_DEVICE>();
                if (!EnumDisplayDevices(null, i, ref dd, 0)) break;

                // Skip anything not on the desktop (headless cards - selecting
                // one crashes the client) and mirroring/phantom drivers, which
                // occupy an entry without being a real GPU.
                if ((dd.StateFlags & ATTACHED_TO_DESKTOP) == 0) continue;
                if ((dd.StateFlags & MIRRORING_DRIVER) != 0) continue;

                string name = (dd.DeviceString ?? "").Trim();
                if (name.Length == 0) continue;

                // One entry per PHYSICAL adapter: a card driving two monitors
                // appears twice here, and dgVoodoo counts the adapter once.
                if (!seen.Add(name)) continue;

                found.Add(new GpuAdapter
                {
                    Name = name,
                    DeviceName = (dd.DeviceName ?? "").Trim(),
                    IsPrimary = (dd.StateFlags & PRIMARY_DEVICE) != 0,
                });
            }

            // dgVoodoo's ordinal 1 was observed to be the adapter driving the
            // PRIMARY display, so sort primary first and keep enumeration order
            // for the rest. This is an inference from testing, not a documented
            // guarantee - if a user reports the wrong card, the ASI's adapter log
            // line names what was actually used, so they can pick the other entry.
            found.Sort((a, b) => b.IsPrimary.CompareTo(a.IsPrimary));
            for (int i = 0; i < found.Count; i++)
                found[i].DgVoodooOrdinal = i + 1;

            return found;
        }

        /// <summary>
        /// Resolves a stored adapter NAME to its current dgVoodoo ordinal.
        /// Returns null when that adapter is no longer present or no longer has a
        /// display attached - callers should fall back to "all" rather than guess,
        /// because a wrong ordinal crashes the client.
        /// </summary>
        public static int? ResolveOrdinal(string storedName)
        {
            if (string.IsNullOrWhiteSpace(storedName)) return null;
            foreach (var a in Enumerate())
                if (string.Equals(a.Name, storedName.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                    return a.DgVoodooOrdinal;
            return null;
        }
    }
}
