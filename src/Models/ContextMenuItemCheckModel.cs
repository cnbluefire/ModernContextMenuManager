using ModernContextMenuManager.Base;
using ModernContextMenuManager.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ModernContextMenuManager.Models
{
    public partial class ContextMenuItemCheckModel : ObservableObject
    {
        private bool enabled;

        public ContextMenuItemCheckModel(ContextMenuItem contextMenuItem, bool enabled, bool canModify)
        {
            ContextMenuItem = contextMenuItem;
            CanModify = canModify;
            this.enabled = enabled;
        }

        public ContextMenuItem ContextMenuItem { get; }

        public bool CanModify { get; }

        public bool Enabled
        {
            get => enabled;
            set => SetProperty(ref enabled, value,
                onPropertyChanging: (oldValue, newValue) =>
                    PackagedComHelper.SetBlockedClsid(ContextMenuItem.Clsid, PackagedComHelper.BlockedClsidType.CurrentUser, !newValue),
                notifyWhenNotChanged: true,
                asyncNotifyWhenNotChanged: true);
        }

        public string DisplayName
        {
            get
            {
                var title = ContextMenuItem.Title;
                if (string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(ContextMenuItem.Id)) title = $"[{ContextMenuItem.Id}]";

                if (!string.IsNullOrEmpty(ContextMenuItem.Type) && !string.IsNullOrEmpty(title))
                {
                    return $"[{ContextMenuItem.Type}] {title}\n{ContextMenuItem.Clsid:B}";
                }
                else if (!string.IsNullOrEmpty(ContextMenuItem.Type))
                {
                    return $"[{ContextMenuItem.Type}]\n{ContextMenuItem.Clsid:B}";
                }
                else if (!string.IsNullOrEmpty(title))
                {
                    return $"{title}\n{ContextMenuItem.Clsid:B}";
                }

                return $"{ContextMenuItem.Clsid:B}";
            }
        }
    }
}
