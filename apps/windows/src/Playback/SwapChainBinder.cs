using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Controls;

namespace Anibel.App.Playback;

/// <summary>
/// WinUI3 SwapChainPanel binding without external D3D wrappers:
/// mpv's `display-swapchain` property returns IDXGISwapChain* (IUnknown*);
/// the panel is cast to ISwapChainPanelNative
/// (Microsoft.UI.Xaml: 63aad0b8-7c24-40ff-85a8-640d944cc325) and the
/// swapchain is attached through its vtable (slot 3).
/// </summary>
internal static class SwapChainBinder
{
    [Guid("63aad0b8-7c24-40ff-85a8-640d944cc325")]
    private interface ISwapChainPanelNative
    {
        [PreserveSig]
        int SetSwapChain(IntPtr swapChain);
    }

    private delegate int SetSwapChainDelegate(IntPtr thisPtr, IntPtr swapChain);

    public static void Attach(SwapChainPanel panel, IntPtr swapChainPtr)
    {
        var native = QueryPanelNative(panel);
        if (native == IntPtr.Zero)
        {
            throw new InvalidOperationException("SwapChainPanel does not support ISwapChainPanelNative");
        }

        try
        {
            var setSwapChain = ReadVtableSlot3(native);
            var hr = setSwapChain(native, swapChainPtr);
            if (hr != 0)
            {
                throw new InvalidOperationException($"SetSwapChain failed: 0x{hr:X8}");
            }
        }
        finally
        {
            Marshal.Release(native);
        }
    }

    public static void Detach(SwapChainPanel panel)
    {
        Attach(panel, IntPtr.Zero);
    }

    private static IntPtr QueryPanelNative(SwapChainPanel panel)
    {
        // panel implements IWinRTObject (CsWinRT) → its IUnknown*
        var thisPtr = ((WinRT.IWinRTObject)panel).NativeObject.ThisPtr;
        var guid = typeof(ISwapChainPanelNative).GUID;
        var hr = Marshal.QueryInterface(thisPtr, in guid, out var ppv);
        return hr != 0 ? IntPtr.Zero : ppv;
    }

    private static SetSwapChainDelegate ReadVtableSlot3(IntPtr interfacePtr)
    {
        var vtable = Marshal.ReadIntPtr(interfacePtr);
        var slot3 = Marshal.ReadIntPtr(vtable, 3 * IntPtr.Size);
        return Marshal.GetDelegateForFunctionPointer<SetSwapChainDelegate>(slot3);
    }
}
