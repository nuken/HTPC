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
}