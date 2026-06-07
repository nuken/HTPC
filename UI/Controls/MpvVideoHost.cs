using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace HTPC.UI.Controls;

/// <summary>
/// A native HWND host that provides a dedicated rendering surface for libmpv.
/// </summary>
public class MpvVideoHost : HwndHost
{
    private const int WS_CHILD = 0x40000000;
    private const int WS_VISIBLE = 0x10000000;
    private const int HOST_ID = 0x00000002;

    [DllImport("user32.dll", EntryPoint = "CreateWindowEx", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(
        int dwExStyle, string lpszClassName, string lpszWindowName,
        int style, int x, int y, int width, int height,
        IntPtr hwndParent, IntPtr hMenu, IntPtr hInst, IntPtr pvParam);

    [DllImport("user32.dll", EntryPoint = "DestroyWindow", CharSet = CharSet.Unicode)]
    private static extern bool DestroyWindow(IntPtr hwnd);

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        // Create a blank, native static window
        IntPtr hwndHost = CreateWindowEx(
            0, "static", "",
            WS_CHILD | WS_VISIBLE,
            0, 0, 0, 0,
            hwndParent.Handle,
            (IntPtr)HOST_ID,
            IntPtr.Zero,
            IntPtr.Zero);

        return new HandleRef(this, hwndHost);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        DestroyWindow(hwnd.Handle);
    }
}