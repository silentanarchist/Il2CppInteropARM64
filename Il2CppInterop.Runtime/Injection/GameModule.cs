using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Il2CppInterop.Common;
using Microsoft.Extensions.Logging;

namespace Il2CppInterop.Runtime.Injection
{
    // Warpforge Revival change.
    // Where the game's code library (libil2cpp.so) sits in memory. Normally it is in the process's
    // module list. Under an ARM translator (Intel Houdini in MuMu and similar PC emulators) .NET leaves
    // the library out of that list, since its pages are not marked as executable for the PC's processor,
    // and the lookup used to throw, which stopped every mod. The library is then looked up in the
    // process's memory map (/proc/self/maps) instead. Only its position and size are needed.
    internal sealed class GameModule
    {
        public string ModuleName { get; }
        public IntPtr BaseAddress { get; }
        public int ModuleMemorySize { get; }

        private GameModule(string name, IntPtr baseAddress, long size)
        {
            ModuleName = name;
            BaseAddress = baseAddress;
            ModuleMemorySize = (int)Math.Min(size, int.MaxValue);
        }

        private static readonly string[] Names = { "GameAssembly.dll", "GameAssembly.so", "UserAssembly.dll", "libil2cpp.so" };

        internal static GameModule Find()
        {
            try
            {
                var m = Process.GetCurrentProcess().Modules.OfType<ProcessModule>()
                    .FirstOrDefault(x => Names.Contains(x.ModuleName));
                if (m != null) return new GameModule(m.ModuleName, m.BaseAddress, m.ModuleMemorySize);
            }
            catch (Exception e)
            {
                Logger.Instance.LogWarning("Could not list the process's modules: {Error}", e.Message);
            }

            var fromMap = FromMemoryMap();
            if (fromMap != null) return fromMap;
            Logger.Instance.LogError("The game's code library was not found in the process's modules or memory map");
            return new GameModule("libil2cpp.so", IntPtr.Zero, 0);
        }

        private static GameModule FromMemoryMap()
        {
            try
            {
                long start = long.MaxValue, end = 0;
                var related = new System.Collections.Generic.List<string>();
                foreach (var line in File.ReadLines("/proc/self/maps"))
                {
                    if (!line.Contains("il2cpp")) continue;
                    if (related.Count < 12) related.Add(line);
                    if (!line.TrimEnd().EndsWith("/libil2cpp.so", StringComparison.Ordinal)) continue;
                    var range = line.Substring(0, line.IndexOf(' '));
                    var dash = range.IndexOf('-');
                    long s = long.Parse(range.Substring(0, dash), NumberStyles.HexNumber);
                    long e = long.Parse(range.Substring(dash + 1), NumberStyles.HexNumber);
                    if (s < start) start = s;
                    if (e > end) end = e;
                }
                foreach (var l in related) Logger.Instance.LogInformation("memory map: {Line}", l);
                if (end == 0) return null;
                Logger.Instance.LogWarning(
                    "libil2cpp.so is not in the process's module list (as under an ARM translator); found it in the memory map at 0x{Base} ({Size} bytes)",
                    start.ToString("X"), end - start);
                return new GameModule("libil2cpp.so", (IntPtr)start, end - start);
            }
            catch (Exception e)
            {
                Logger.Instance.LogWarning("Could not read the memory map: {Error}", e.Message);
                return null;
            }
        }
    }
}
