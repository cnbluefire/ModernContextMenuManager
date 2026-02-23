using Avalonia.Controls;
using Microsoft.Win32;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Win32.Foundation;

namespace ModernContextMenuManager.Helpers
{
    public static class PackagedComHelper
    {
        private const string SubKey_PackagedCom_Package = "SOFTWARE\\Classes\\PackagedCom\\Package\\";
        private const string SubKey_BlockedClsids = "Software\\Microsoft\\Windows\\CurrentVersion\\Shell Extensions\\Blocked";

        public static ComPackage[] GetAllComPackages()
        {
            try
            {
                using (var subKey = Registry.LocalMachine.OpenSubKey(SubKey_PackagedCom_Package, false))
                {
                    if (subKey != null)
                    {
                        var names = subKey.GetSubKeyNames();
                        if (names.Length > 0)
                        {
                            var list = new List<ComPackage>(names.Length);
                            for (int i = 0; i < names.Length; i++)
                            {
                                using (var subKey2 = subKey.OpenSubKey($"{names[i]}\\Class", false))
                                {
                                    if (subKey2 != null)
                                    {
                                        var names2 = subKey2.GetSubKeyNames();
                                        var list2 = new List<ComPackageComInfo>(names2.Length);
                                        for (int j = 0; j < names2.Length; j++)
                                        {
                                            if (Guid.TryParse(names2[j], out var clsid))
                                            {
                                                using (var subKey3 = subKey2.OpenSubKey(names2[j], false))
                                                {
                                                    if (subKey3 != null)
                                                    {
                                                        var dllPath = (string)subKey3.GetValue("DllPath", "");
                                                        if (!string.IsNullOrEmpty(dllPath) && dllPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                                                        {
                                                            var serverId = (int)subKey3.GetValue("ServerId", 0);
                                                            var threading = (int)subKey3.GetValue("Threading", 0);
                                                            list2.Add(new ComPackageComInfo(clsid, dllPath, threading switch
                                                            {
                                                                0 => ApartmentState.STA,
                                                                1 => ApartmentState.MTA,
                                                                _ => ApartmentState.Unknown
                                                            }));
                                                        }
                                                    }
                                                }
                                            }
                                        }

                                        list.Add(new ComPackage(names[i], [.. list2]));
                                    }
                                }
                            }

                            return [.. list];
                        }
                    }
                }
            }
            catch { }
            return [];
        }

        public static BlockedClsid[] GetBlockedClsids()
        {
            List<BlockedClsid>? list = null;
            try
            {
                using (var subKey = Registry.LocalMachine.OpenSubKey(SubKey_BlockedClsids, false))
                {
                    if (subKey != null)
                    {
                        var names = subKey.GetValueNames();
                        for (int i = 0; i < names.Length; i++)
                        {
                            if (Guid.TryParse(names[i], out var clsid))
                            {
                                if (list == null) list = new List<BlockedClsid>(names.Length * 2);
                                list.Add(new BlockedClsid(clsid, BlockedClsidType.LocalMachine));
                            }
                        }
                    }
                }
            }
            catch { }
            try
            {
                using (var subKey = Registry.CurrentUser.OpenSubKey(SubKey_BlockedClsids, false))
                {
                    if (subKey != null)
                    {
                        var names = subKey.GetValueNames();
                        for (int i = 0; i < names.Length; i++)
                        {
                            if (Guid.TryParse(names[i], out var clsid))
                            {
                                if (list == null) list = new List<BlockedClsid>(names.Length * 2);
                                list.Add(new BlockedClsid(clsid, BlockedClsidType.CurrentUser));
                            }
                        }
                    }
                }
            }
            catch { }

            if (list != null)
            {
                return [.. list.Distinct()];
            }

            return [];
        }

        public static bool SetBlockedClsid(Guid clsid, BlockedClsidType type, bool blocked)
        {
            try
            {
                RegistryKey rootKey = type switch
                {
                    BlockedClsidType.LocalMachine => Registry.LocalMachine,
                    _ => Registry.CurrentUser
                };

                using (var subKey = rootKey.CreateSubKey(SubKey_BlockedClsids, true))
                {
                    var name = clsid.ToString("B").ToUpperInvariant();
                    if (blocked)
                    {
                        var oldValue = subKey.GetValue(name);
                        if (oldValue is null)
                        {
                            subKey.SetValue(name, "Blocked by ContextMenuManager");
                        }
                        return true;
                    }
                    else
                    {
                        subKey.DeleteValue(name);
                        return true;
                    }
                }
            }
            catch { }

            return false;
        }

        private static ConcurrentDictionary<(Guid clsid, string type), string?> cachedExplorerCommandTitle = new();

        public static void DeleteMCMMFolder()
        {
            var tmpPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MCMM_Dir");
            if (System.IO.Directory.Exists(tmpPath))
            {
                try
                {
                    System.IO.Directory.Delete(tmpPath, true);
                }
                catch { }
            }
        }

        public static unsafe string? TryGetExplorerCommandTitle(Guid clsid, string type)
        {
            static HRESULT GetShellItemArray(string type, out Windows.Win32.UI.Shell.IShellItemArray* ppv)
            {
                ppv = null;

                if (type == @"Directory\Background")
                {
                    fixed (Windows.Win32.UI.Shell.IShellItemArray** ptr = &ppv)
                    {
                        return Windows.Win32.PInvoke.SHCreateShellItemArrayFromIDLists(0, null, ptr);
                    }
                }

                var path = "";

                var tmpPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MCMM_Dir");
                if (!System.IO.Directory.Exists(tmpPath))
                {
                    System.IO.Directory.CreateDirectory(tmpPath);
                }

                if (type == "Directory")
                {
                    path = tmpPath;
                }
                else
                {
                    if (type == "*")
                    {
                        path = System.IO.Path.Combine(tmpPath, "MCMM");
                    }
                    else
                    {
                        path = System.IO.Path.Combine(tmpPath, $"MCMM{type}");
                    }
                    if (!System.IO.File.Exists(path))
                    {
                        System.IO.File.Create(path).Dispose();
                    }
                }

                void* pShellItem = null;
                try
                {
                    var hr = Windows.Win32.PInvoke.SHCreateItemFromParsingName(path, null, Windows.Win32.UI.Shell.IShellItem.IID_Guid, out pShellItem);
                    if (hr.Succeeded)
                    {
                        ((Windows.Win32.UI.Shell.IShellItem*)pShellItem)->GetDisplayName(Windows.Win32.UI.Shell.SIGDN.SIGDN_NORMALDISPLAY, out var name);

                        hr = Windows.Win32.PInvoke.SHCreateShellItemArrayFromShellItem((Windows.Win32.UI.Shell.IShellItem*)pShellItem, Windows.Win32.UI.Shell.IShellItemArray.IID_Guid, out var pShellItemArray);
                        if (hr.Succeeded)
                        {
                            ppv = (Windows.Win32.UI.Shell.IShellItemArray*)pShellItemArray;
                        }
                    }
                    return hr;
                }
                finally
                {
                    if (pShellItem != null) Marshal.Release((nint)pShellItem);
                }
            }

            return cachedExplorerCommandTitle.GetOrAdd((clsid, type), static (key) =>
            {
                void* pExplorerCommand = null;
                Windows.Win32.UI.Shell.IShellItemArray* pShellItemArray = null;
                PWSTR pName = default;

                try
                {
                    var hr = Windows.Win32.PInvoke.CoCreateInstance(
                        key.clsid,
                        null,
                        Windows.Win32.System.Com.CLSCTX.CLSCTX_ALL,
                        Windows.Win32.UI.Shell.IExplorerCommand.IID_Guid,
                        out pExplorerCommand);

                    if (hr.Succeeded)
                    {
                        try
                        {
                            hr = GetShellItemArray(key.type, out pShellItemArray);
                            if (hr.Succeeded)
                            {
                                hr = ((Windows.Win32.UI.Shell.IExplorerCommand*)pExplorerCommand)->GetTitle(pShellItemArray, out pName);
                                if (hr.Succeeded && pName.Length > 0)
                                {
                                    return pName.ToString();
                                }
                            }
                        }
                        catch { }
                    }
                }
                catch { }
                finally
                {
                    if (pExplorerCommand != null) Marshal.Release((nint)pExplorerCommand);
                    if (pShellItemArray != null) Marshal.Release((nint)pShellItemArray);
                    if (pName.Value != null) Marshal.FreeCoTaskMem((nint)pName.Value);
                }

                return null;
            });

        }

        public record struct ComPackage(string PackageFullName, ComPackageComInfo[] Clsids);

        public record struct ComPackageComInfo(Guid Clsid, string DllPath, ApartmentState ThreadingMode);

        public record struct BlockedClsid(Guid Clsid, BlockedClsidType Type);

        public enum BlockedClsidType
        {
            CurrentUser,
            LocalMachine
        }
    }
}
