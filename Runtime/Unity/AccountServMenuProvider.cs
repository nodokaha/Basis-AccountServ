using Basis.BasisUI;
using UnityEngine;

namespace BasisAccountServ
{
    /// <summary>
    /// Adds an Account entry to the native Basis main-menu hotbar.
    /// </summary>
    public sealed class AccountServMenuProvider : BasisMenuActionProvider<BasisMainMenu>
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Register()
        {
            BasisMenuBase<BasisMainMenu>.AddProvider(new AccountServMenuProvider());
        }

        public override bool Hidden => false;
        public override string Title => "Account";
        public override string IconAddress => string.Empty;
        public override int Order => 800;

        public override void RunAction()
        {
            BasisAccountServWindow.Open();
        }
    }
}
