using System.Runtime.InteropServices;

namespace Anibel.App.Core;

/// <summary>
/// P/Invoke surface of `anibel_core.dll` (crates/anibel-core, C ABI JSON contract).
/// </summary>
internal static class AnibelCoreNative
{
    private const string Dll = "anibel_core.dll";

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int anibel_core_request_begin(long handle, long id);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void anibel_core_cancel(long handle, long id);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    internal static extern long anibel_core_init([MarshalAs(UnmanagedType.LPUTF8Str)] string configJson);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void anibel_core_shutdown(long handle);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr anibel_core_call(long handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string requestJson);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr anibel_core_events(long handle);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void anibel_core_free(IntPtr ptr);
}
