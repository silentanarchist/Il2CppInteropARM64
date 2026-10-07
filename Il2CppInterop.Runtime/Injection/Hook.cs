using System;
using System.Runtime.InteropServices;
using Il2CppInterop.Common;
using Microsoft.Extensions.Logging;

namespace Il2CppInterop.Runtime.Injection
{
    internal abstract class Hook<T> where T : Delegate
    {
        private bool _isApplied;
        private T _detour;
        private T _method;
        private T _original;

        public T Original => _original;

        public abstract string TargetMethodName { get; }
        public abstract T GetDetour();
        public abstract IntPtr FindTargetMethod();

        /// <summary>Which quick test, if any, tells that a call does not concern a mod-added class (see FastPath).</summary>
        public virtual int FastPathKind => 0;

        public virtual void TargetMethodNotFound()
        {
            throw new Exception($"Required target method {TargetMethodName} not found");
        }

        public void ApplyHook()
        {
            if (_isApplied) return;

            var methodPtr = KnownTargets.TryGet(TargetMethodName);
            if (methodPtr == IntPtr.Zero) methodPtr = FindTargetMethod();

            if (methodPtr == IntPtr.Zero)
            {
                TargetMethodNotFound();
                return;
            }

            Logger.Instance.LogTrace("{MethodName} found: 0x{MethodPtr}", TargetMethodName, methodPtr.ToInt64().ToString("X2"));

            _detour = GetDetour();
            // Warpforge Revival change: see FastPath
            if (!FastPath.TryApply(methodPtr, _detour, FastPathKind, TargetMethodName, o => _original = o))
                Detour.Apply(methodPtr, _detour, out _original);
            _method = Marshal.GetDelegateForFunctionPointer<T>(methodPtr);
            _isApplied = true;
        }
    }
}
