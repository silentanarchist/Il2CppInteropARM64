using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Il2CppInterop.Common;
using Microsoft.Extensions.Logging;

namespace Il2CppInterop.Runtime.Injection
{
    // Warpforge Revival change.
    // The generic searches for the game's internal functions were written for x64 code and pick the
    // wrong functions in this ARM64 build. The game will never be updated again, so for the one known
    // build (Warhammer 40,000: Warpforge 1.35.0, Android arm64) the functions are listed here by their
    // position in libil2cpp.so. Every entry is checked against the code actually found there, and
    // anything that does not match falls back to the generic search.
    internal static unsafe class KnownTargets
    {
        private const long AnchorOffset = 0x2D32014; // il2cpp_image_get_class

        private static readonly Dictionary<string, (long offset, uint first, uint second)> s_Warpforge135 = new()
        {
            { "GenericMethod::GetMethod", (0x2D4A2D0, 0xD100C3FF, 0xF9000BFE) },
            { "MetadataCache::GetTypeInfoFromTypeDefinitionIndex", (0x2D7A86C, 0xF81A0FFE, 0xA9016FFC) },
            { "Class::GetDefaultFieldValue", (0x2D7A214, 0xD10103FF, 0xA9015FFE) },
            { "Class::FromIl2CppType", (0x2D8D4D8, 0xA9BE57FE, 0xA9014FF4) },
            { "Class::FromName", (0x2D83EE0, 0xD10243FF, 0xF90033FE) },
            { "Class::Init", (0x2D8D9B0, 0xD10083FF, 0xA9014FFE) },
        };

        private static bool s_Checked;
        private static long s_Base;

        private static long GetBase()
        {
            if (s_Checked) return s_Base;
            s_Checked = true;
            try
            {
                if (RuntimeInformation.ProcessArchitecture != Architecture.Arm64) return 0;
                if (!InjectorHelpers.TryGetIl2CppExport("il2cpp_image_get_class", out var anchor)) return 0;
                var candidate = (long)anchor - AnchorOffset;
                // two more exported functions must sit where this build has them
                if (!InjectorHelpers.TryGetIl2CppExport("il2cpp_class_from_name", out var a) || (long)a - candidate != 0x2D31458) return 0;
                if (!InjectorHelpers.TryGetIl2CppExport("il2cpp_field_static_get_value", out var b) || (long)b - candidate != 0x2D31B10) return 0;
                s_Base = candidate;
                Logger.Instance.LogInformation("Known game build recognised; using its listed function positions");
            }
            catch (Exception e)
            {
                Logger.Instance.LogWarning("Known-build check failed: {Error}", e.Message);
            }
            return s_Base;
        }

        internal static IntPtr TryGet(string name)
        {
            var b = GetBase();
            if (b == 0 || !s_Warpforge135.TryGetValue(name, out var entry)) return IntPtr.Zero;
            var p = (uint*)(b + entry.offset);
            if (p[0] != entry.first || p[1] != entry.second)
            {
                Logger.Instance.LogWarning("{Name}: listed position does not hold the expected code, searching instead", name);
                return IntPtr.Zero;
            }
            return (IntPtr)p;
        }
    }
}
