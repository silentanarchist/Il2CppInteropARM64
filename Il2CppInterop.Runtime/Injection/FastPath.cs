using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Il2CppInterop.Common;
using Microsoft.Extensions.Logging;

namespace Il2CppInterop.Runtime.Injection
{
    // Warpforge Revival change.
    // Two of the loader's hooks sit on engine functions the game calls tens of millions of times
    // while it loads (measured: 54 million calls while unpacking one inventory). Each call used to
    // leave the game's native code for the loader's managed code and come back, which on a phone
    // costs over a microsecond a time - more than a minute in total. Almost none of those calls
    // concern a class added by a mod, and that can be told from one bit of the argument.
    // So a few machine instructions are placed in front of the managed hook: the ordinary case goes
    // straight back to the game's own function, and only the rare case goes on to managed code.
    internal static unsafe class FastPath
    {
        /// <summary>The first argument points at a value; managed code is only needed when that value is negative.</summary>
        internal const int NegativeValueBehindPointer = 1;
        /// <summary>The first argument is a 32-bit number; managed code is only needed when it is negative.</summary>
        internal const int NegativeNumber = 2;

        [DllImport("libc", SetLastError = true)] private static extern nint mmap(nint addr, nuint length, int prot, int flags, int fd, nint offset);
        [DllImport("libc", SetLastError = true)] private static extern int mprotect(nint addr, nuint length, int prot);

        private static readonly uint[] ViaPointer =
        {
            0xF9400010, // ldr  x16, [x0]
            0xB7F80090, // tbnz x16, #63, slow
            0x580000D1, // ldr  x17, slots
            0xF9400631, // ldr  x17, [x17, #8]   the game's own function
            0xD61F0220, // br   x17
            0x58000071, // slow: ldr x17, slots
            0xF9400231, // ldr  x17, [x17]       the managed hook
            0xD61F0220, // br   x17
        };

        private static readonly uint[] ViaNumber =
        {
            0x37F80080, // tbnz w0, #31, slow
            0x580000F1, // ldr  x17, slots
            0xF9400631, // ldr  x17, [x17, #8]
            0xD61F0220, // br   x17
            0x58000091, // slow: ldr x17, slots
            0xF9400231, // ldr  x17, [x17]
            0xD61F0220, // br   x17
            0xD503201F, // nop
        };

        internal static bool TryApply<T>(IntPtr target, T detour, int kind, string name, Action<T> setOriginal) where T : Delegate
        {
            try
            {
                if (kind == 0 || RuntimeInformation.ProcessArchitecture != Architecture.Arm64) return false;
                var attach = Type.GetType("MelonLoader.InternalUtils.BootstrapInterop, MelonLoader")
                    ?.GetMethod("NativeHookAttachDirect", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                if (attach == null) return false;

                const int ReadWrite = 3, ReadExecute = 5, PrivateAnonymous = 0x22;
                nint slots = mmap(0, 4096, ReadWrite, PrivateAnonymous, -1, 0);
                nint code = mmap(0, 4096, ReadWrite, PrivateAnonymous, -1, 0);
                if (slots == -1 || code == -1 || slots == 0 || code == 0) return false;

                nint managed = Marshal.GetFunctionPointerForDelegate(detour);
                var slot = (nint*)slots;
                slot[0] = managed;
                slot[1] = managed; // until the game's own function is known, everything takes the long way

                var words = kind == NegativeValueBehindPointer ? ViaPointer : ViaNumber;
                var w = (uint*)code;
                for (int i = 0; i < words.Length; i++) w[i] = words[i];
                *(nint*)(code + words.Length * 4) = slots;
                if (mprotect(code, 4096, ReadExecute) != 0) return false;

                nint address = target;
                attach.Invoke(null, new object[] { (IntPtr)(&address), (IntPtr)code });
                if (address == target || address == 0) return false;

                setOriginal(Marshal.GetDelegateForFunctionPointer<T>(address));
                slot[1] = address;
                Logger.Instance.LogInformation("{Name}: short cut in place (most calls no longer leave the game's own code)", name);
                return true;
            }
            catch (Exception e)
            {
                Logger.Instance.LogWarning("{Name}: short cut not possible ({Error}); using the ordinary hook", name, e.Message);
                return false;
            }
        }
    }
}
