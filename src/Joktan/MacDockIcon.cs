using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia.Platform;

namespace Joktan;

/// <summary>
/// Sets the macOS Dock icon. A packaged .app takes its icon from the bundle, but when the app is
/// run as a plain executable macOS shows a generic icon unless it is set while running.
/// </summary>
internal static class MacDockIcon
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    private static readonly Uri Icon = new("avares://Joktan.Core/Assets/joktan-icon-mac.png");

    public static void Apply()
    {
        if (!OperatingSystem.IsMacOS())
            return;

        try
        {
            using var stream = AssetLoader.Open(Icon);
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            var bytes = buffer.ToArray();

            // [NSApp setApplicationIconImage: [[NSImage alloc] initWithData: [NSData dataWithBytes:length:]]]
            var data = SendBytes(objc_getClass("NSData"), sel_registerName("dataWithBytes:length:"), bytes, (nuint)bytes.Length);
            var image = Send(Send(objc_getClass("NSImage"), sel_registerName("alloc")), sel_registerName("initWithData:"), data);
            if (image == IntPtr.Zero)
                return;

            var app = Send(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
            Send(app, sel_registerName("setApplicationIconImage:"), image);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or IOException)
        {
            // The app works without it; the Dock just shows the default icon.
        }
    }

    [DllImport(ObjC)]
    private static extern IntPtr objc_getClass(string name);

    [DllImport(ObjC)]
    private static extern IntPtr sel_registerName(string name);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr argument);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendBytes(IntPtr receiver, IntPtr selector, byte[] bytes, nuint length);
}
