namespace Il2CppInterop.Runtime.Injection
{
    // Warpforge Revival change: counts how often the game's engine lands in each of the loader's
    // hooks, to find out where start-up time goes on a phone. Plain counters; an occasional lost
    // count from two threads at once does not matter here.
    public static class HookStats
    {
        public static long FromIl2CppType, FromName, GetFieldDefaultValue, GetTypeInfo, GetMethod, PatchedCalls, PatchedArguments;

        public static string Snapshot() =>
            $"type-from-signature {FromIl2CppType}, type-by-name {FromName}, field-default {GetFieldDefaultValue}, " +
            $"type-by-index {GetTypeInfo}, generic-method {GetMethod}, patched-method calls {PatchedCalls}, checked arguments {PatchedArguments}";
    }
}
