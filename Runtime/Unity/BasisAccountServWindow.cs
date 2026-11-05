using System.Collections.Generic;
using UnityEngine;

namespace BasisAccountServ
{
    /// <summary>
    /// Small, independent AccountServ panel. F8 opens/closes it.
    /// It uses the AccountServ plugin channel and never reads, consumes, or suppresses public chat.
    /// </summary>
    public sealed class BasisAccountServWindow : MonoBehaviour
    {
        private enum Page
        {
            Register,
            Identify,
            Status,
            Logout,
            Operator
        }

        private static readonly string[] PageNames =
        {
            "Register", "Identify", "Status", "Logout", "Operator"
        };

        private static BasisAccountServWindow instance;
        private readonly List<string> messages = new List<string>();
        private Rect windowRect = new Rect(24, 80, 440, 420);
        private Vector2 scroll;
        private bool visible;
        private Page page;
        private string accountName = string.Empty;
        private string password = string.Empty;
        private string targetUuid = string.Empty;
        private int roleIndex = 1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            if (instance != null) return;
            GameObject host = new GameObject("BasisAccountServWindow");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<BasisAccountServWindow>();
        }

        private void OnEnable()
        {
            AccountServClient.ResponseReceived += OnResponse;
            AddMessage("AccountServ ready. Press F8 to open this private panel.");
        }

        private void OnDisable()
        {
            AccountServClient.ResponseReceived -= OnResponse;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F8))
                visible = !visible;
        }

        private void OnResponse(string response)
        {
            AddMessage(response);
        }

        private void AddMessage(string message)
        {
            lock (messages)
            {
                messages.Add(message);
                if (messages.Count > 80)
                    messages.RemoveAt(0);
            }
        }

        private void OnGUI()
        {
            if (!visible) return;
            windowRect = GUI.Window(GetInstanceID(), windowRect, DrawWindow, "AccountServ - Private server session");
        }

        private void DrawWindow(int id)
        {
            GUILayout.Label(AccountServClient.IsAvailable
                ? "Connected to an AccountServ-enabled server."
                : "This server has not advertised AccountServ, or the connection is not ready.");

            lock (messages)
            {
                scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(145));
                foreach (string message in messages)
                    GUILayout.Label(message, GUI.skin.box);
                GUILayout.EndScrollView();
            }

            page = (Page)GUILayout.Toolbar((int)page, PageNames);
            GUILayout.Space(8);

            if (page == Page.Register || page == Page.Identify)
            {
                GUILayout.Label("Account name");
                accountName = GUILayout.TextField(accountName, 24);
                GUILayout.Label("Password (processed locally; password itself is never sent)");
                password = GUILayout.PasswordField(password, '*', 256);
                if (GUILayout.Button(page == Page.Register ? "Register account" : "Identify"))
                {
                    bool sent = page == Page.Register
                        ? AccountServClient.Register(accountName, password)
                        : AccountServClient.Identify(accountName, password);
                    ReportSendResult(sent);
                    password = string.Empty;
                }
            }
            else if (page == Page.Status)
            {
                GUILayout.Label("Check the AccountServ session bound to your authenticated Basis identity.");
                if (GUILayout.Button("Check status"))
                    ReportSendResult(AccountServClient.GetStatus());
            }
            else if (page == Page.Logout)
            {
                GUILayout.Label("End the AccountServ session for this Basis identity.");
                if (GUILayout.Button("Log out"))
                    ReportSendResult(AccountServClient.Logout());
            }
            else if (page == Page.Operator)
            {
                GUILayout.Label("Target Basis UUID");
                targetUuid = GUILayout.TextField(targetUuid, 128);
                GUILayout.Label("Role");
                roleIndex = GUILayout.SelectionGrid(roleIndex, new[] { "admin", "moderator", "none" }, 3);
                if (GUILayout.Button("Apply role"))
                    ReportSendResult(AccountServClient.SetRole(targetUuid, new[] { "admin", "moderator", "none" }[roleIndex]));
                GUILayout.Label("Only an existing Basis admin can change roles. The server verifies this.");
            }

            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Clear messages"))
            {
                lock (messages) messages.Clear();
            }
            if (GUILayout.Button("Close"))
                visible = false;
            GUILayout.EndHorizontal();
            GUI.DragWindow(new Rect(0, 0, 10000, 24));
        }

        private void ReportSendResult(bool sent)
        {
            if (!sent)
                AddMessage("Request not sent: wait for the server connection and AccountServ plugin subscription.");
        }
    }
}
