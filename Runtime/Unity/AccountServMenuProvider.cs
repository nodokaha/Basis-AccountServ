using System;
using System.Collections.Generic;
using Basis.BasisUI;
using Basis.Scripts.Drivers;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;

namespace BasisAccountServ
{
    /// <summary>
    /// Adds an Account entry to the native Basis main-menu hotbar.
    /// </summary>
    /// <remarks>
    /// The class is <c>partial</c> because Unity's AutoStaticsCleanup generator can only
    /// emit its reset method for partial types (the same convention Basis uses).
    ///
    /// Registration is deliberately redundant and idempotent. Every built-in Basis provider
    /// registers after scene load, while this package also registers before scene load;
    /// if the shared provider list or the menu is reset between those points the entry
    /// would silently disappear. <see cref="EnsureRegistered"/> is therefore called from
    /// several entry points, and from <see cref="BasisMainMenu.OpenStateChanged"/> so the
    /// hotbar is repaired every time the menu is opened.
    /// </remarks>
    [AutoStaticsCleanup]
    public sealed partial class AccountServMenuProvider : BasisMenuActionProvider<BasisMainMenu>
    {
        private static AccountServMenuProvider instance;

        internal static bool ContainsProvider(
            IEnumerable<BasisMenuActionProvider<BasisMainMenu>> providers)
        {
            return FindProvider(providers) != null;
        }

        internal static AccountServMenuProvider FindProvider(
            IEnumerable<BasisMenuActionProvider<BasisMainMenu>> providers)
        {
            foreach (BasisMenuActionProvider<BasisMainMenu> provider in providers)
            {
                if (provider is AccountServMenuProvider accountServ)
                    return accountServ;
            }

            return null;
        }

        internal static void EnsureRegistered(string reason)
        {
            try
            {
                HookMenuLifecycle();
                Register();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[AccountServ] Main-menu registration failed during " + reason + ": " + exception.Message);
                Debug.LogException(exception);
            }
        }

        internal static void HookMenuLifecycle()
        {
            BasisMainMenu.OpenStateChanged -= OnMainMenuOpenStateChanged;
            BasisMainMenu.OpenStateChanged += OnMainMenuOpenStateChanged;
        }

        private static void OnMainMenuOpenStateChanged(bool open)
        {
            if (!open)
                return;
            try
            {
                Register();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[AccountServ] Unable to repair the Account button when the menu opened: " + exception.Message);
                Debug.LogException(exception);
            }
        }

        internal static bool HasBoundButton(
            BasisMenuBase<BasisMainMenu> menu,
            AccountServMenuProvider provider)
        {
            return menu != null &&
                   provider != null &&
                   provider.BoundMenu == menu &&
                   provider.BoundButton != null &&
                   menu.ProviderButtons.Contains(provider.BoundButton);
        }

        internal static void Register()
        {
            List<BasisMenuActionProvider<BasisMainMenu>> providers =
                BasisMenuBase<BasisMainMenu>.Providers;
            AccountServMenuProvider registered = FindProvider(providers);
            if (registered == null)
            {
                instance ??= new AccountServMenuProvider();
                BasisMenuBase<BasisMainMenu>.AddProvider(instance);
                registered = FindProvider(providers);
                if (registered == null)
                {
                    Debug.LogWarning("[AccountServ] Main-menu provider registration was rejected. Basis may have suppressed AccountServMenuProvider.");
                    return;
                }
            }
            else
            {
                instance = registered;
            }

            BasisMenuBase<BasisMainMenu> menu = BasisMenuBase<BasisMainMenu>.Instance;
            if (menu == null || HasBoundButton(menu, registered))
                return;

            menu.BindProvidersToButtons();
            if (!HasBoundButton(menu, registered))
            {
                Debug.LogWarning("[AccountServ] Provider is registered, but its hotbar button was not created. Check BasisMainMenu.ProviderButtonParent and PanelButton.CreateNew.");
            }
        }

        public override bool Hidden => false;
        public override string Title => "Account";
        public override string IconAddress => AddressableAssets.Sprites.Avatars;
        public override int Order => 800;

        public override void RunAction()
        {
            BasisAccountServWindow.Open();
        }
    }
}
