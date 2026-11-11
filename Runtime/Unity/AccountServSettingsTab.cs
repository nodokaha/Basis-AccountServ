using Basis.BasisUI;

namespace BasisAccountServ
{
    /// <summary>
    /// Registers AccountServ as a native tab in Basis Settings. The SettingsProvider owns
    /// navigation and panel lifetime; this package only supplies the tab content.
    /// </summary>
    internal static class AccountServSettingsTab
    {
        internal const string TabKey = "accountserv.settings.tab";

        internal static void Register()
        {
            for (int i = SettingsProvider.ExternalTabs.Count - 1; i >= 0; i--)
            {
                if (SettingsProvider.ExternalTabs[i].TabName == TabKey)
                    SettingsProvider.ExternalTabs.RemoveAt(i);
            }

            SettingsProvider.ExternalTabs.Add((TabKey, BuildTab));
        }

        private static PanelTabPage BuildTab(PanelTabGroup tabs)
        {
            PanelTabPage page = PanelTabPage.CreateVertical(tabs.Descriptor.ContentParent);
            page.Descriptor.SetTitle("AccountServ");

            AccountServSettingsTabLifetime lifetime =
                page.Descriptor.ContentParent.gameObject.AddComponent<AccountServSettingsTabLifetime>();
            lifetime.Build(page);

            return page;
        }

        internal static void OpenFromShortcut()
        {
            SettingsProvider.OpenToTab(TabKey);
        }
    }

}
