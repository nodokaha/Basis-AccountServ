using Basis.BasisUI;
using Basis.Scripts.Drivers;
using UnityEngine;

namespace BasisAccountServ
{
    /// <summary>
    /// Adds an Account entry to the native Basis main-menu hotbar.
    /// </summary>
    public sealed class AccountServMenuProvider : BasisMenuActionProvider<BasisMainMenu>
    {
        private static bool IsRegistered =>
            BasisMenuBase<BasisMainMenu>.Providers.Exists(provider => provider is AccountServMenuProvider);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InstallRegistrationGuard()
        {
            // Basis providers are registered during startup too. Keep a small persistent
            // guard so this provider is re-added if Basis recreates/clears its static list.
            var host = new GameObject(nameof(AccountServMenuProviderRegistrationGuard));
            Object.DontDestroyOnLoad(host);
            host.AddComponent<AccountServMenuProviderRegistrationGuard>();
        }

        internal static void EnsureRegistered()
        {
            if (IsRegistered)
                return;

            BasisMenuBase<BasisMainMenu>.AddProvider(new AccountServMenuProvider());
            Debug.Log($"[AccountServ] Registered main-menu provider. Provider count: {BasisMenuBase<BasisMainMenu>.Providers.Count}.");
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

    /// <summary>
    /// Ensures AccountServ remains registered after Basis initializes or resets its menu providers.
    /// </summary>
    internal sealed class AccountServMenuProviderRegistrationGuard : MonoBehaviour
    {
        private void Awake()
        {
            AccountServMenuProvider.EnsureRegistered();
        }

        private void Update()
        {
            AccountServMenuProvider.EnsureRegistered();
        }
    }
}
