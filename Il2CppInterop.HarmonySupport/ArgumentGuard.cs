using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Il2CppInterop.Common;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Runtime;
using Microsoft.Extensions.Logging;

namespace Il2CppInterop.HarmonySupport;

// Warpforge Revival change.
// On ARM64 the game's compiler does not bother to pass an argument that the called method never
// reads: the register just holds whatever was in it. That is invisible to the game, but a patched
// method hands every argument to managed code, and wrapping a leftover value as a game object
// crashes. Before an object or text argument is wrapped it is therefore checked: it must be readable
// memory that starts with a real class. Anything else is an argument the method does not use, and
// is passed on as "nothing" (or, for the object the method belongs to, as a blank object of the
// right class so the original method can still be called).
internal static unsafe class ArgumentGuard
{
    [DllImport("libc", SetLastError = true)] private static extern int pipe(int* fds);
    [DllImport("libc", SetLastError = true)] private static extern nint write(int fd, void* buf, nint count);
    [DllImport("libc", SetLastError = true)] private static extern nint read(int fd, void* buf, nint count);

    private static readonly object Gate = new();
    private static readonly HashSet<nint> KnownClasses = new();
    private static readonly HashSet<string> Warned = new();
    private static bool _ready, _active;
    private static int _readFd = -1, _writeFd = -1, _selfOffset = -1;

    /// <summary>Reads one pointer without risking a crash: the kernel refuses unreadable addresses.</summary>
    private static bool TryRead(nint address, out nint value)
    {
        value = 0;
        nint v = 0;
        if (write(_writeFd, (void*)address, sizeof(nint)) != sizeof(nint)) return false;
        if (read(_readFd, &v, sizeof(nint)) != sizeof(nint)) return false;
        value = v;
        return true;
    }

    private static void Setup()
    {
        _ready = true;
        try
        {
            if (RuntimeInformation.ProcessArchitecture != Architecture.Arm64) return;
            int* fds = stackalloc int[2];
            if (pipe(fds) != 0) return;
            _readFd = fds[0];
            _writeFd = fds[1];

            // Every class holds a pointer to itself; find where, using three classes we know are real.
            nint obj = Il2CppClassPointerStore<Il2CppSystem.Object>.NativeClassPtr;
            nint str = Il2CppClassPointerStore<string>.NativeClassPtr;
            nint arr = obj != 0 ? IL2CPP.il2cpp_array_class_get(obj, 1) : 0;
            if (obj == 0 || str == 0 || arr == 0) return;
            for (int off = 0x100; off >= 0; off -= 8)
            {
                if (TryRead(obj + off, out var a) && a == obj && TryRead(str + off, out var b) && b == str
                    && TryRead(arr + off, out var c) && c == arr)
                {
                    _selfOffset = off;
                    break;
                }
            }
            _active = _selfOffset >= 0;
            Logger.Instance.LogInformation(_active
                ? "Argument check for patched methods is on (class marker at 0x{Offset})"
                : "Argument check for patched methods could not be set up ({Offset})", _selfOffset.ToString("X"));
        }
        catch (Exception e)
        {
            Logger.Instance.LogWarning("Argument check for patched methods could not be set up: {Error}", e.Message);
        }
    }

    private static bool LooksLikeObject(nint p, out nint klass)
    {
        klass = 0;
        if ((p & 7) != 0 || (ulong)p < 0x10000 || ((ulong)p >> 48) != 0) return false;
        if (!TryRead(p, out klass)) return false;
        if ((klass & 7) != 0 || (ulong)klass < 0x10000 || ((ulong)klass >> 48) != 0) return false;
        if (KnownClasses.Contains(klass)) return true;
        if (!TryRead(klass + _selfOffset, out var self) || self != klass) return false;
        KnownClasses.Add(klass);
        return true;
    }

    private static void Warn(string what)
    {
        if (Warned.Add(what))
            Logger.Instance.LogInformation("Patched method received an unused {What}; passing a stand-in", what);
    }

    public static T Get<T>(IntPtr ptr, int isThis)
    {
        Il2CppInterop.Runtime.Injection.HookStats.PatchedArguments++;
        if (isThis != 0) Il2CppInterop.Runtime.Injection.HookStats.PatchedCalls++;
        if (ptr == IntPtr.Zero && isThis == 0) return default;
        lock (Gate)
        {
            if (!_ready) Setup();
            if (!_active && ptr == IntPtr.Zero) return default;
            if (_active)
            {
                nint expected = Il2CppClassPointerStore<T>.NativeClassPtr;
                bool ok = LooksLikeObject(ptr, out var klass);
                if (ok && isThis != 0 && expected != 0 && klass != expected && !IL2CPP.il2cpp_class_is_assignable_from(expected, klass))
                    ok = false;
                if (!ok)
                {
                    Warn((isThis != 0 ? "'this' of type " : "argument of type ") + typeof(T).FullName);
                    if (isThis == 0 || expected == 0) return default;
                    try { ptr = IL2CPP.il2cpp_object_new(expected); }
                    catch { return default; }
                    if (ptr == IntPtr.Zero) return default;
                }
            }
        }
        return Il2CppObjectPool.Get<T>(ptr);
    }

    public static string GetString(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero) return null;
        lock (Gate)
        {
            if (!_ready) Setup();
            if (_active)
            {
                nint str = Il2CppClassPointerStore<string>.NativeClassPtr;
                if (!LooksLikeObject(ptr, out var klass) || (str != 0 && klass != str))
                {
                    Warn("text argument");
                    return null;
                }
            }
        }
        return IL2CPP.Il2CppStringToManaged(ptr);
    }
}
