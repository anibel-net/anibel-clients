using System.Runtime.InteropServices;

namespace Anibel.App.Playback;

/// <summary>P/Invoke surface of libmpv (patched build with d3d11 composition support).</summary>
internal static class MpvNative
{
    private const string Dll = "libmpv-2.dll";

    public const int MpvFormatString = 1;
    public const int MpvFormatFlag = 3;
    public const int MpvFormatInt64 = 4;
    public const int MpvFormatDouble = 5;

    // event ids (mpv v0.41 client.h)
    public const int EventNone = 0;
    public const int EventEndFile = 7;
    public const int EventFileLoaded = 8;
    public const int EventVideoReconfig = 17;
    public const int EventPlaybackRestart = 21;
    public const int EventPropertyChange = 22;

    [StructLayout(LayoutKind.Sequential)]
    public struct MpvEvent
    {
        public int event_id;
        public int error;
        public ulong reply_userdata;
        public IntPtr data;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MpvEventProperty
    {
        public IntPtr name;
        public int format;
        public IntPtr data;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MpvEventEndFile
    {
        public int reason; // 0 = EOF, 2 = stop, 3 = quit, 4 = error
        public int error;
    }

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr mpv_create();

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_initialize(IntPtr handle);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void mpv_terminate_destroy(IntPtr handle);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_set_option_string(IntPtr handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mpv_get_property")]
    private static extern int MpvGetProperty(IntPtr handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int format, IntPtr data);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mpv_set_property")]
    private static extern int MpvSetProperty(IntPtr handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int format, IntPtr data);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_command(IntPtr handle, IntPtr args);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_command_string(IntPtr handle, IntPtr command);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_observe_property(IntPtr handle, long userdata, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int format);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr mpv_wait_event(IntPtr handle, double timeoutSeconds);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void mpv_wakeup(IntPtr handle);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void mpv_free(IntPtr data);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr mpv_error_string(int error);

    // ---- helpers ----------------------------------------------------------

    public static int Command(IntPtr handle, params string[] args)
    {
        var argv = args
            .Select(a => Marshal.StringToCoTaskMemUTF8(a))
            .Append(IntPtr.Zero)
            .ToArray();
        var pinned = GCHandle.Alloc(argv, GCHandleType.Pinned);
        try
        {
            return mpv_command(handle, pinned.AddrOfPinnedObject());
        }
        finally
        {
            pinned.Free();
            foreach (var p in argv)
            {
                if (p != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(p);
                }
            }
        }
    }

    public static int CommandString(IntPtr handle, string cmd)
    {
        var c = Marshal.StringToCoTaskMemUTF8(cmd);
        try
        {
            return mpv_command_string(handle, c);
        }
        finally
        {
            Marshal.FreeCoTaskMem(c);
        }
    }

    /// <summary>MPV_FORMAT_INT64 — also used for pointer-sized values (display-swapchain).</summary>
    public static long? GetInt64(IntPtr handle, string name)
    {
        var valuePtr = Marshal.AllocHGlobal(8);
        try
        {
            if (MpvGetProperty(handle, name, MpvFormatInt64, valuePtr) != 0)
            {
                return null;
            }
            return Marshal.PtrToStructure<long>(valuePtr);
        }
        finally
        {
            Marshal.FreeHGlobal(valuePtr);
        }
    }

    public static double? GetDouble(IntPtr handle, string name)
    {
        var valuePtr = Marshal.AllocHGlobal(8);
        try
        {
            if (MpvGetProperty(handle, name, MpvFormatDouble, valuePtr) != 0)
            {
                return null;
            }
            return Marshal.PtrToStructure<double>(valuePtr);
        }
        finally
        {
            Marshal.FreeHGlobal(valuePtr);
        }
    }

    public static bool? GetFlag(IntPtr handle, string name)
    {
        var valuePtr = Marshal.AllocHGlobal(4);
        try
        {
            if (MpvGetProperty(handle, name, MpvFormatFlag, valuePtr) != 0)
            {
                return null;
            }
            return Marshal.PtrToStructure<int>(valuePtr) != 0;
        }
        finally
        {
            Marshal.FreeHGlobal(valuePtr);
        }
    }

    public static bool SetDouble(IntPtr handle, string name, double value)
    {
        var valuePtr = Marshal.AllocHGlobal(8);
        try
        {
            Marshal.StructureToPtr(value, valuePtr, false);
            return MpvSetProperty(handle, name, MpvFormatDouble, valuePtr) == 0;
        }
        finally
        {
            Marshal.FreeHGlobal(valuePtr);
        }
    }

    public static bool SetInt64(IntPtr handle, string name, long value)
    {
        var valuePtr = Marshal.AllocHGlobal(8);
        try
        {
            Marshal.StructureToPtr(value, valuePtr, false);
            return MpvSetProperty(handle, name, MpvFormatInt64, valuePtr) == 0;
        }
        finally
        {
            Marshal.FreeHGlobal(valuePtr);
        }
    }

    public static string? GetString(IntPtr handle, string name)
    {
        var box = Marshal.AllocHGlobal(IntPtr.Size);
        try
        {
            if (MpvGetProperty(handle, name, MpvFormatString, box) != 0)
            {
                return null;
            }
            var strPtr = Marshal.ReadIntPtr(box);
            if (strPtr == IntPtr.Zero)
            {
                return null;
            }
            try
            {
                return Marshal.PtrToStringUTF8(strPtr);
            }
            finally
            {
                mpv_free(strPtr);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(box);
        }
    }

    public static bool SetFlag(IntPtr handle, string name, bool value)
    {
        var valuePtr = Marshal.AllocHGlobal(4);
        try
        {
            Marshal.StructureToPtr(value ? 1 : 0, valuePtr, false);
            return MpvSetProperty(handle, name, MpvFormatFlag, valuePtr) == 0;
        }
        finally
        {
            Marshal.FreeHGlobal(valuePtr);
        }
    }
}
