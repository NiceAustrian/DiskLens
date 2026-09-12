using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;
using DiskLens.Core.Platform;
using Microsoft.Extensions.Logging;

namespace DiskLens.Scanners.Windows;

/// <summary>
/// Explorer's context menu for a file or folder via IContextMenu, with the app's own items merged
/// in on top. Submenus such as "Send to" and "Open with" are populated lazily by the shell through
/// WM_INITMENUPOPUP/WM_DRAWITEM/WM_MEASUREITEM, which is why the host window's message stream is
/// hooked while the menu is open. Uses source-generated COM so it survives trimming.
/// Must run on an STA thread (the UI thread is one).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class ShellContextMenu(ILogger<ShellContextMenu> logger) : INativeContextMenu
{
    private static readonly StrategyBasedComWrappers ComWrappers = new();

    public bool IsSupported => true;

    public int Show(string path, NativeWindow window, int clientX, int clientY, IReadOnlyList<NativeMenuItem> customItems)
    {
        var hwnd = window.Handle;
        var pt = new POINT { X = clientX, Y = clientY };
        ClientToScreen(hwnd, ref pt);

        nint pidl = 0, childPidl = 0, folderPtr = 0, menuPtr = 0, hMenu = 0;
        IDisposable? hook = null;
        var chosenCustom = -1;
        try
        {
            var hr = SHParseDisplayName(path, 0, out pidl, 0, out _);
            if (hr < 0) throw Marshal.GetExceptionForHR(hr)!;

            var iidShellFolder = IID_IShellFolder;
            hr = SHBindToParent(pidl, ref iidShellFolder, out folderPtr, out childPidl);
            if (hr < 0) throw Marshal.GetExceptionForHR(hr)!;
            var folder = (IShellFolder)ComWrappers.GetOrCreateObjectForComInstance(folderPtr, CreateObjectFlags.UniqueInstance);

            var iidContextMenu = IID_IContextMenu;
            hr = folder.GetUIObjectOf(hwnd, 1, ref childPidl, ref iidContextMenu, 0, out menuPtr);
            if (hr < 0) throw Marshal.GetExceptionForHR(hr)!;
            var menuObj = ComWrappers.GetOrCreateObjectForComInstance(menuPtr, CreateObjectFlags.UniqueInstance);
            var menu = (IContextMenu)menuObj;
            var menu2 = menuObj as IContextMenu2;
            var menu3 = menuObj as IContextMenu3;

            hMenu = CreatePopupMenu();

            // Our own items first, then a separator, then whatever the shell offers.
            var position = 0u;
            for (var i = 0; i < customItems.Count; i++)
            {
                var item = customItems[i];
                if (item.Label is null)
                    InsertMenuW(hMenu, position++, MF_BYPOSITION | MF_SEPARATOR, 0, null);
                else
                    InsertMenuW(hMenu, position++, MF_BYPOSITION | MF_STRING | (item.IsEnabled ? 0 : MF_GRAYED), (nint)(CustomFirst + (uint)i), item.Label);
            }
            if (customItems.Count > 0) InsertMenuW(hMenu, position++, MF_BYPOSITION | MF_SEPARATOR, 0, null);

            hr = menu.QueryContextMenu(hMenu, position, CmdFirst, CmdLast, CMF_NORMAL | CMF_EXPLORE);
            if (hr < 0) throw Marshal.GetExceptionForHR(hr)!;

            // Let the shell handle owner-drawn items and lazy submenus while the menu is up.
            hook = window.AddMessageFilter?.Invoke((h, msg, w, l) =>
            {
                if (msg is not (WM_INITMENUPOPUP or WM_DRAWITEM or WM_MEASUREITEM or WM_MENUCHAR)) return null;
                if (menu3 is not null && menu3.HandleMenuMsg2(msg, w, l, out var result) >= 0) return result;
                if (menu2 is not null && menu2.HandleMenuMsg(msg, w, l) >= 0) return 0;
                return null;
            });

            logger.LogDebug("Shell menu for {Path}: {Items} items", path, GetMenuItemCount(hMenu));
            SetForegroundWindow(hwnd);
            var cmd = TrackPopupMenuEx(hMenu, TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_LEFTALIGN, pt.X, pt.Y, hwnd, 0);
            if (cmd >= CustomFirst)
            {
                chosenCustom = (int)(cmd - CustomFirst);
            }
            else if (cmd >= CmdFirst && cmd <= CmdLast)
            {
                var info = new CMINVOKECOMMANDINFOEX
                {
                    cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFOEX>(),
                    fMask = CMIC_MASK_UNICODE | CMIC_MASK_PTINVOKE,
                    hwnd = hwnd,
                    lpVerb = (nint)(cmd - CmdFirst),     // MAKEINTRESOURCE(id offset)
                    lpVerbW = (nint)(cmd - CmdFirst),
                    nShow = SW_SHOWNORMAL,
                    ptInvoke = pt,
                };
                hr = menu.InvokeCommand(ref info);
                if (hr < 0) logger.LogWarning("Shell command {Cmd} for {Path} failed: 0x{Hr:X8}", cmd, path, hr);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Explorer context menu for {Path} failed", path);
        }
        finally
        {
            hook?.Dispose();
            if (hMenu != 0) DestroyMenu(hMenu);
            if (menuPtr != 0) Marshal.Release(menuPtr);
            if (folderPtr != 0) Marshal.Release(folderPtr);
            if (pidl != 0) Marshal.FreeCoTaskMem(pidl);
        }
        return chosenCustom;
    }

    // Interop ------------------------------------------------------------------------------------
    private const uint CmdFirst = 1, CmdLast = 0x6FFF;
    private const uint CustomFirst = 0x7000;
    private const uint CMF_NORMAL = 0x0, CMF_EXPLORE = 0x4;
    private const uint TPM_LEFTALIGN = 0x0, TPM_RIGHTBUTTON = 0x2, TPM_RETURNCMD = 0x100;
    private const uint CMIC_MASK_UNICODE = 0x4000, CMIC_MASK_PTINVOKE = 0x20000000;
    private const uint MF_STRING = 0x0, MF_GRAYED = 0x1, MF_SEPARATOR = 0x800, MF_BYPOSITION = 0x400;
    private const int SW_SHOWNORMAL = 1;
    private const uint WM_DRAWITEM = 0x002B, WM_MEASUREITEM = 0x002C, WM_INITMENUPOPUP = 0x0117, WM_MENUCHAR = 0x0120;

    private static readonly Guid IID_IShellFolder = new("000214E6-0000-0000-C000-000000000046");
    private static readonly Guid IID_IContextMenu = new("000214E4-0000-0000-C000-000000000046");

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CMINVOKECOMMANDINFOEX
    {
        public int cbSize;
        public uint fMask;
        public nint hwnd;
        public nint lpVerb;
        public nint lpParameters;
        public nint lpDirectory;
        public int nShow;
        public uint dwHotKey;
        public nint hIcon;
        public nint lpTitle;
        public nint lpVerbW;
        public nint lpParametersW;
        public nint lpDirectoryW;
        public nint lpTitleW;
        public POINT ptInvoke;
    }

    // Source-generated COM: only the slots we call are typed; the rest just hold their vtable positions.
    [GeneratedComInterface, Guid("000214E6-0000-0000-C000-000000000046")]
    internal partial interface IShellFolder
    {
        [PreserveSig] int ParseDisplayName();
        [PreserveSig] int EnumObjects();
        [PreserveSig] int BindToObject();
        [PreserveSig] int BindToStorage();
        [PreserveSig] int CompareIDs();
        [PreserveSig] int CreateViewObject();
        [PreserveSig] int GetAttributesOf();
        [PreserveSig] int GetUIObjectOf(nint hwndOwner, uint cidl, ref nint apidl, ref Guid riid, nint reserved, out nint ppv);
        [PreserveSig] int GetDisplayNameOf();
        [PreserveSig] int SetNameOf();
    }

    [GeneratedComInterface, Guid("000214E4-0000-0000-C000-000000000046")]
    internal partial interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(nint hMenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint flags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX info);
        [PreserveSig] int GetCommandString();
    }

    [GeneratedComInterface, Guid("000214F4-0000-0000-C000-000000000046")]
    internal partial interface IContextMenu2 : IContextMenu
    {
        [PreserveSig] int HandleMenuMsg(uint msg, nint wParam, nint lParam);
    }

    [GeneratedComInterface, Guid("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719")]
    internal partial interface IContextMenu3 : IContextMenu2
    {
        [PreserveSig] int HandleMenuMsg2(uint msg, nint wParam, nint lParam, out nint result);
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHParseDisplayName(string name, nint bindingContext, out nint pidl, uint attributesIn, out uint attributesOut);

    [LibraryImport("shell32.dll")]
    private static partial int SHBindToParent(nint pidl, ref Guid riid, out nint ppv, out nint childPidl);

    [LibraryImport("user32.dll")] private static partial nint CreatePopupMenu();
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool DestroyMenu(nint hMenu);
    [LibraryImport("user32.dll")] private static partial int GetMenuItemCount(nint hMenu);
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool InsertMenuW(nint hMenu, uint position, uint flags, nint id, string? text);
    [LibraryImport("user32.dll")] private static partial uint TrackPopupMenuEx(nint hMenu, uint flags, int x, int y, nint hwnd, nint parameters);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool SetForegroundWindow(nint hwnd);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool ClientToScreen(nint hwnd, ref POINT pt);
}
