using Basis.BasisUI;
using NUnit.Framework;

namespace BasisAccountServ.Tests
{
    public sealed class AccountServSettingsTabTests
    {
        [SetUp]
        public void SetUp()
        {
            RemoveAccountServTab();
        }

        [TearDown]
        public void TearDown()
        {
            RemoveAccountServTab();
        }

        private static void RemoveAccountServTab()
        {
            for (int i = SettingsProvider.ExternalTabs.Count - 1; i >= 0; i--)
            {
                if (SettingsProvider.ExternalTabs[i].TabName == AccountServSettingsTab.TabKey)
                    SettingsProvider.ExternalTabs.RemoveAt(i);
            }
        }

        [Test]
        public void Register_AddsAccountServToSettingsTabs()
        {
            AccountServSettingsTab.Register();

            Assert.That(
                SettingsProvider.ExternalTabs.Exists(tab => tab.TabName == AccountServSettingsTab.TabKey),
                Is.True);
        }

        [Test]
        public void Register_DoesNotAddDuplicateTabs()
        {
            AccountServSettingsTab.Register();
            AccountServSettingsTab.Register();

            Assert.That(
                SettingsProvider.ExternalTabs.FindAll(tab => tab.TabName == AccountServSettingsTab.TabKey).Count,
                Is.EqualTo(1));
        }

        [Test]
        public void ShortcutUsesSettingsTabLocalizationKey()
        {
            Assert.That(AccountServSettingsTab.TabKey, Is.EqualTo("accountserv.settings.tab"));
        }
    }
}
