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

        internal static bool RegisterIfMissing(
            IEnumerable<BasisMenuActionProvider<BasisMainMenu>> providers,
            Action register)
        {
            if (ContainsProvider(providers))
                return false;

            register?.Invoke();
            return true;
        }

        /// <summary>
        /// Called by AccountServClient.Initialize so menu registration shares the
        /// same guaranteed package startup path as the client message handler.
        /// </summary>
        internal static void Register()
        {
            if (instance == null)
                instance = new AccountServMenuProvider();

            bool registered = RegisterIfMissing(
                BasisMenuBase<BasisMainMenu>.Providers,
                () => BasisMenuBase<BasisMainMenu>.AddProvider(instance));

            Debug.Log(registered
                ? $"[AccountServ] Main-menu provider registered. Provider count: {BasisMenuBase<BasisMainMenu>.Providers.Count}."
                : "[AccountServ] Main-menu provider was already registered.");
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
