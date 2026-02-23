using ModernContextMenuManager.Base;
using ModernContextMenuManager.Helpers;
using ModernContextMenuManager.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tmds.DBus.Protocol;
using static ModernContextMenuManager.Helpers.PackagedComHelper;

namespace ModernContextMenuManager.ViewModels
{
    public class MainWindowViewModel : ObservableObject
    {
        private ComPackage[]? comPackages;
        private BlockedClsid[]? blockedClsids;

        private IReadOnlyList<PackagedAppModel>? apps;
        private string searchingText = "";
        private AsyncRelayCommand<string>? searchCommand;

        public IReadOnlyList<PackagedAppModel>? Apps
        {
            get => apps;
            private set => SetProperty(ref apps, value);
        }

        public string SearchingText
        {
            get => searchingText;
            set => SetProperty(ref searchingText, value);
        }

        public AsyncRelayCommand<string> SearchCommand => searchCommand ??= new AsyncRelayCommand<string>(async search =>
        {
            search = search?.Trim();

            Apps = await Task.Run(async () =>
            {
                comPackages = PackagedComHelper.GetAllComPackages();
                blockedClsids = PackagedComHelper.GetBlockedClsids();

                var dict = blockedClsids
                    .DistinctBy(c => c.Clsid)
                    .ToDictionary(c => c.Clsid, c => c.Type);

                var list = new List<PackagedAppModel>(comPackages.Length);
                for (int i = 0; i < comPackages.Length; i++)
                {
                    if (comPackages[i].Clsids.Length > 0)
                    {
                        var packageInfo = PackageManager.GetPackageInfoByFullName(comPackages[i].PackageFullName);
                        if (packageInfo != null)
                        {
                            var appInfo = await PackageManager.GetPackageAppInfoAsync(packageInfo);
                            if (appInfo != null && appInfo.ContextMenuItems.Count > 0)
                            {
                                if (string.IsNullOrEmpty(search)
                                    || appInfo.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase)
                                    || appInfo.ContextMenuItems.Any(
                                        c => GuidContains(c.Clsid, search)
                                                         || c.Type.Contains(search, StringComparison.OrdinalIgnoreCase)
                                                         || c.Id.Contains(search, StringComparison.OrdinalIgnoreCase)
                                                         || c.Title?.Contains(search, StringComparison.OrdinalIgnoreCase) is true))
                                {
                                    list.Add(new PackagedAppModel(appInfo, packageInfo, dict));
                                }
                            }
                        }
                    }
                }

                return list;

                static bool GuidContains(Guid guid, string text) =>
                    guid.ToString("B").Contains(text, StringComparison.OrdinalIgnoreCase)
                    || guid.ToString("N").Contains(text, StringComparison.OrdinalIgnoreCase);
            });
        });
    }
}
