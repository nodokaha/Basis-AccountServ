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
    [AutoStaticsCleanup]
    public sealed class AccountServMenuProvider : BasisMenuActionProvider<BasisMainMenu>
    {
        private static AccountServMenuProvider instance;

        internal static bool ContainsProvider(
            IEnumerable<BasisMenuActionProvider<BasisMainMenu>> providers)
        {
            foreach (BasisMenuActionProvider<BasisMainMenu> provider in providers)
            {
                if (provider is AccountServMenuProvider)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Register independently of AccountServClient initialization.
        /// Basis' AddProvider API rebinds the hotbar when the menu already exists.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        internal static void Register()
        {
            List<BasisMenuActionProvider<BasisMainMenu>> providers =
                BasisMenuBase<BasisMainMenu>.Providers;

            // Basis' AddProvider does not deduplicate providers, so guard by type first.
            if (ContainsProvider(providers))
            {
                Debug.Log("[AccountServ] Main-menu provider already registered.");
                return;
            }

            instance ??= new AccountServMenuProvider();
            BasisMenuBase<BasisMainMenu>.AddProvider(instance);

            // AddProvider can reject a provider type suppressed by Basis. Do not report
            // success until the provider is actually present in the shared provider list.
            if (!ContainsProvider(providers))
            {
                Debug.LogWarning(
                    "[AccountServ] Main-menu provider registration was rejected. " +
                    "Basis may have suppressed AccountServMenuProvider.");
                return;
            }

            if (BasisMenuBase<BasisMainMenu>.Instance != null)
            {
                // AddProvider synchronously calls BindProvidersToButtons for an existing menu.
                // A missing BoundButton at this point means the menu could not create the entry.
                if (instance.BoundMenu != BasisMenuBase<BasisMainMenu>.Instance ||
                    instance.BoundButton == null ||
                    !BasisMenuBase<BasisMainMenu>.Instance.ProviderButtons.Contains(instance.BoundButton))
                {
                    Debug.LogWarning(
                        "[AccountServ] Provider is registered, but its hotbar button was not created. " +
                        "Check BasisMainMenu.ProviderButtonParent and PanelButton.CreateNew.");
                    return;
                }

                Debug.Log("[AccountServ] Main-menu provider registered and hotbar button bound.");
                return;
            }

            // The provider remains in the static list and BasisMainMenu binds it from
            // its constructor when the main-menu instance is created.
            Debug.Log(
                "[AccountServ] Main-menu provider registered. " +
                "The hotbar button will be created when BasisMainMenu is initialized.");
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
