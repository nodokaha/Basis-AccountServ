using UnityEngine;
using UnityEngine.InputSystem;

namespace BasisAccountServ
{
    /// <summary>
    /// Keyboard shortcut bridge for the native AccountServ Basis provider page.
    /// This component deliberately owns no UI; BasisMainMenu owns the page lifecycle.
    /// </summary>
    public sealed class BasisAccountServWindow : MonoBehaviour
    {
        private static BasisAccountServWindow instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            if (instance != null) return;

            GameObject host = new GameObject("BasisAccountServShortcut");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<BasisAccountServWindow>();
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f8Key.wasPressedThisFrame)
                AccountServMenuProvider.ToggleFromShortcut();
        }
    }
}
