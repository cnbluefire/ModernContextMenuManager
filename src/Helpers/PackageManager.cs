using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Windows.Win32;
using Windows.Win32.Storage.Packaging.Appx;

namespace ModernContextMenuManager.Helpers
{
    internal static class PackageManager
    {
        private static string[]? defaultLanguages;

        public unsafe static PackageInfo? GetPackageInfoByFullName(string packageFullName)
        {
            const uint PACKAGE_FILTER_HEAD = 0x00000010;

            fixed (char* pPfn = packageFullName)
            {
                Windows.Win32.Storage.Packaging.Appx._PACKAGE_INFO_REFERENCE* reference = null;

                try
                {

                    var err = Windows.Win32.PInvoke.OpenPackageInfoByFullName(pPfn, 0, &reference);
                    if (err == Windows.Win32.Foundation.WIN32_ERROR.ERROR_SUCCESS)
                    {
                        uint bufferLength = 0;
                        uint count = 0;

                        err = Windows.Win32.PInvoke.GetPackageInfo(
                            reference,
                            PACKAGE_FILTER_HEAD,
                            &bufferLength,
                            null,
                            &count);

                        if (count > 0 && err == Windows.Win32.Foundation.WIN32_ERROR.ERROR_INSUFFICIENT_BUFFER)
                        {
                            using (var memoryOwner = MemoryPool<byte>.Shared.Rent((int)bufferLength))
                            using (var memoryHandle = memoryOwner.Memory.Pin())
                            {
                                err = Windows.Win32.PInvoke.GetPackageInfo(
                                    reference,
                                    PACKAGE_FILTER_HEAD,
                                    &bufferLength,
                                    (byte*)memoryHandle.Pointer,
                                    &count);

                                if (err == Windows.Win32.Foundation.WIN32_ERROR.ERROR_SUCCESS)
                                {
                                    var packageInfo = (PACKAGE_INFO*)memoryHandle.Pointer;
                                    var version = &packageInfo->packageId.version.Anonymous.Anonymous;

                                    return new PackageInfo(
                                        packageInfo->path.ToString(),
                                        packageInfo->packageFullName.ToString(),
                                        packageInfo->packageFamilyName.ToString(),
                                        new PackageId(
                                            (PackageId.ProcessorArchitecture)packageInfo->packageId.processorArchitecture,
                                            new Version(version->Major, version->Minor, version->Build, version->Revision),
                                            packageInfo->packageId.name.ToString(),
                                            packageInfo->packageId.publisher.ToString(),
                                            packageInfo->packageId.resourceId.ToString(),
                                            packageInfo->packageId.publisherId.ToString()));
                                }
                            }
                        }
                    }
                }
                finally
                {
                    if (reference != null) Windows.Win32.PInvoke.ClosePackageInfo(reference);
                }
            }

            return null;
        }


        public static async Task<AppInfo?> GetPackageAppInfoAsync(PackageInfo packageInfo, CancellationToken cancellationToken = default)
        {
            var xmlDocument = await GetAppxManifestDocumentAsync(packageInfo.PackageInstallLocation, cancellationToken);

            if (xmlDocument != null)
            {
                IReadOnlyList<Guid>? clsids = null;

                var namespaceManager = new XmlNamespaceManager(xmlDocument.NameTable);

                namespaceManager.AddNamespace("default", "http://schemas.microsoft.com/appx/manifest/foundation/windows10");
                namespaceManager.AddNamespace("desktop4", "http://schemas.microsoft.com/appx/manifest/desktop/windows10/4");
                namespaceManager.AddNamespace("desktop5", "http://schemas.microsoft.com/appx/manifest/desktop/windows10/5");
                namespaceManager.AddNamespace("uap", "http://schemas.microsoft.com/appx/manifest/uap/windows10");

                var nodes1 = xmlDocument.SelectNodes("//desktop4:FileExplorerContextMenus//desktop4:Verb", namespaceManager);
                var nodes2 = xmlDocument.SelectNodes("//desktop4:FileExplorerContextMenus//desktop5:Verb", namespaceManager);

                if ((nodes1?.Count ?? 0) + (nodes2?.Count ?? 0) > 0)
                {

                    var list = new List<Guid>((nodes1?.Count ?? 0) + (nodes2?.Count ?? 0));
                    if (nodes1 != null)
                    {
                        for (int i = 0; i < nodes1.Count; i++)
                        {
                            var clsid = nodes1[i]?.Attributes?["Clsid"]?.Value;
                            if (Guid.TryParse(clsid, out var guid))
                            {
                                list.Add(guid);
                            }
                        }
                    }

                    if (nodes2 != null)
                    {
                        for (int i = 0; i < nodes2.Count; i++)
                        {
                            var clsid = nodes2[i]?.Attributes?["Clsid"]?.Value;
                            if (Guid.TryParse(clsid, out var guid))
                            {
                                list.Add(guid);
                            }
                        }
                    }

                    clsids = [.. list.Distinct()];
                }

                var logoNode = xmlDocument.SelectSingleNode("//default:Properties/default:Logo", namespaceManager);
                string logo = logoNode?.InnerText ?? "";
                var logoFullPath = Path.Combine(packageInfo.PackageInstallLocation, logo);

                if (!File.Exists(logoFullPath))
                {
                    var logoDirectory = Path.GetDirectoryName(logoFullPath);
                    logoFullPath = "";
                    var logoKey = Path.GetFileNameWithoutExtension(logo);
                    var ext = Path.GetExtension(logo);
                    if (Directory.Exists(logoDirectory))
                    {
                        var files = Directory.GetFiles(logoDirectory, $"{logoKey}*{ext}");
                        logoFullPath = files?.FirstOrDefault(c => !c.Contains("contrast"));
                        if (string.IsNullOrEmpty(logoFullPath)) logoFullPath = files?.FirstOrDefault() ?? "";
                    }
                }

                var appNodes = xmlDocument.SelectNodes("//uap:VisualElements", namespaceManager);

                if (appNodes != null && appNodes.Count > 0)
                {
                    foreach (XmlNode appNode in appNodes.OfType<XmlNode>().OrderBy(c => c.Attributes?["AppListEntry"]?.Value == "none" ? 1 : 0))
                    {
                        var displayName = appNode.Attributes?["DisplayName"]?.Value ?? "";

                        if (displayName.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase) is true)
                        {
                            var displayName2 = GetDisplayNameFromResource(packageInfo.PackageFullName, displayName);
                            if (!string.IsNullOrEmpty(displayName2)) displayName = displayName2;
                        }

                        return new(displayName, logoFullPath, appNode.Attributes?["AppListEntry"]?.Value != "none", clsids ?? []);
                    }
                }

                if (clsids != null) return new AppInfo("", logoFullPath, false, clsids);
            }
            return null;

            static string? GetDisplayNameFromResource(string packageFullName, string resourceUri)
            {
                const int BufferLength = 1000;

                using var owner = MemoryPool<char>.Shared.Rent(BufferLength);
                var span = owner.Memory.Span[..BufferLength];
                var hr = PInvoke.SHLoadIndirectString($"@{{{packageFullName}? {resourceUri}}}", span);
                if (hr == 0x80073B17)
                {
                    resourceUri = resourceUri.Replace("ms-resource:", "ms-resource:Resources/", StringComparison.OrdinalIgnoreCase);
                    hr = PInvoke.SHLoadIndirectString($"@{{{packageFullName}? {resourceUri}}}", span);
                }
                if (hr.Succeeded)
                {
                    return span.TrimEnd('\0').ToString();
                }
                return null;
            }
        }

        private static async Task<XmlDocument?> GetAppxManifestDocumentAsync(string packageInstallLocation, CancellationToken cancellationToken = default)
        {
            try
            {
                var manifestPath = Path.Combine(packageInstallLocation, "AppxManifest.xml");
                if (File.Exists(manifestPath))
                {
                    var contents = await File.ReadAllTextAsync(manifestPath, cancellationToken);

                    var xmlDocument = new XmlDocument();
                    xmlDocument.LoadXml(contents);

                    return xmlDocument;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { }
            return null;
        }

        private unsafe static string[] GetPreferredUILanguages()
        {
            if (defaultLanguages != null) return defaultLanguages;

            const uint MUI_LANGUAGE_NAME = 0x8;
            const uint MUI_MERGE_SYSTEM_FALLBACK = 0x10;
            const uint MUI_MERGE_USER_FALLBACK = 0x20;

            uint langCount = 0;
            uint bufferLen = 0;

            if (Windows.Win32.PInvoke.GetThreadPreferredUILanguages(
                 MUI_MERGE_SYSTEM_FALLBACK | MUI_MERGE_USER_FALLBACK | MUI_LANGUAGE_NAME,
                 &langCount,
                 default,
                 &bufferLen))
            {
                var buffer = new char[bufferLen];
                fixed (char* pBuffer = buffer)
                {
                    if (Windows.Win32.PInvoke.GetThreadPreferredUILanguages(
                        MUI_MERGE_SYSTEM_FALLBACK | MUI_MERGE_USER_FALLBACK | MUI_LANGUAGE_NAME,
                        &langCount,
                        pBuffer,
                        &bufferLen))
                    {
                        bool enFlag = false;
                        bool enUSFlag = false;

                        var langs = new List<string>((int)langCount);
                        for (int start = 0, i = 0; i < bufferLen; i++)
                        {
                            if (buffer[i] == '\0')
                            {
                                if (i - start > 0)
                                {
                                    var lang = new string(buffer, start, i - start);
                                    langs.Add(lang);

                                    if (!enFlag && string.Equals(lang, "en", StringComparison.OrdinalIgnoreCase)) enFlag = true;
                                    else if (!enUSFlag && string.Equals(lang, "en-US", StringComparison.OrdinalIgnoreCase)) enUSFlag = true;
                                    else if (string.Equals(lang, "zh-Hans", StringComparison.OrdinalIgnoreCase)) langs.Add("zh-CHS");
                                    else if (string.Equals(lang, "zh-Hant", StringComparison.OrdinalIgnoreCase)) langs.Add("zh-CHT");
                                }
                                start = i + 1;
                            }
                        }

                        defaultLanguages = [.. langs];
                    }
                }
            }

            if (defaultLanguages == null) defaultLanguages = [];

            return defaultLanguages;
        }
    }

    public record class AppInfo(string DisplayName, string IconPath, bool AppListEntry, IReadOnlyList<Guid> ContextMenuGuids);

    public record class PackageInfo(
        string PackageInstallLocation,
        string PackageFullName,
        string PackageFamilyName,
        PackageId PackageId);

    public record PackageId(
        PackageId.ProcessorArchitecture Architecture,
        Version Version,
        string Name,
        string Publisher,
        string? ResourceId,
        string PublisherId)
    {
        public enum ProcessorArchitecture : uint
        {
            /// <summary>
            /// The ARM processor architecture.
            /// </summary>
            Arm = 5,

            /// <summary>
            /// The Arm64 processor architecture.
            /// </summary>
            Arm64 = 12,

            /// <summary>
            /// A neutral processor architecture.
            /// </summary>
            Neutral = 11,

            /// <summary>
            /// An unknown processor architecture.
            /// </summary>
            Unknown = 65535,

            /// <summary>
            /// The x64 processor architecture.
            /// </summary>
            X64 = 9,

            /// <summary>
            /// The x86 processor architecture.
            /// </summary>
            X86 = 0,

            /// <summary>
            /// The Arm64 processor architecture emulating the X86 architecture.
            /// </summary>
            X86OnArm64 = 14,
        }
    }
}
