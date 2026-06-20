using System;
using System.Runtime.InteropServices;

namespace HTPC.Core.Interop;

/// <summary>
/// Direct P/Invoke bindings for the native libmpv C-API.
/// </summary>
internal static partial class Libmpv
{
    private const string LibraryName = "mpv-2.dll";

    [LibraryImport(LibraryName)]
    public static partial IntPtr mpv_create();

    [LibraryImport(LibraryName)]
    public static partial int mpv_initialize(IntPtr ctx);

    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int mpv_set_option_string(IntPtr ctx, string name, string data);

    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int mpv_command(IntPtr ctx, IntPtr args);

    [LibraryImport(LibraryName)]
    public static partial void mpv_terminate_destroy(IntPtr ctx);
	
	[LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int mpv_set_property_string(IntPtr ctx, string name, string data);

    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int mpv_command_string(IntPtr ctx, string args);
	
	// format 5 = MPV_FORMAT_DOUBLE
    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int mpv_get_property(IntPtr ctx, string name, int format, out double data);
	
	// --- NEW: MPV EVENT STRUCTS ---
    [StructLayout(LayoutKind.Sequential)]
    public struct mpv_event
    {
        public int event_id;
        public int error;
        public ulong reply_userdata;
        public IntPtr data;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct mpv_event_property
    {
        [MarshalAs(UnmanagedType.LPUTF8Str)]
        public string name;
        public int format;
        public IntPtr data;
    }

    // --- NEW: EVENT LOOP IMPORTS ---
    [LibraryImport(LibraryName)]
    public static partial IntPtr mpv_wait_event(IntPtr ctx, double timeout);

    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int mpv_observe_property(IntPtr ctx, ulong reply_userdata, string name, int format);
}