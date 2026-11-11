using System;
using System.Collections.Generic;
using Basis.BasisUI;
using Basis.Scripts.Drivers;
using TMPro;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;

namespace BasisAccountServ
{
    /// <summary>
    /// Opens AccountServ as a native Basis provider page. The provider is only the entry point;
    /// the account UI itself is built from Basis panel components and does not depend on IMGUI.
    /// </summary>
    [AutoStaticsCleanup]
    public sealed partial class AccountServMenuProvider : BasisMenuActionProvider<BasisMainMenu>
    {
        private static AccountServMenuProvider instance;

        private BasisMenuPanel panel;
        private PanelElementDescriptor feedback;
        private PanelTextField accountNameField;
        private PanelTextField passwordField;
        private PanelTextField targetUuidField;
        private PanelDropdown roleDropdown;

        internal static bool ContainsProvider(IEnumerable<BasisMenuActionProvider<BasisMainMenu>> providers)
            => FindProvider(providers) != null;

        internal static AccountServMenuProvider FindProvider(
            IEnumerable<BasisMenuActionProvider<BasisMainMenu>> providers)
        {
            foreach (BasisMenuActionProvider<BasisMainMenu> provider in providers)
                if (provider is AccountServMenuProvider accountServ)
                    return accountServ;
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
            if (!open) return;
            try { Register(); }
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
            return menu != null && provider != null &&
                   provider.BoundMenu == menu && provider.BoundButton != null &&
                   menu.ProviderButtons.Contains(provider.BoundButton);
        }

        internal static void Register()
        {
            List<BasisMenuActionProvider<BasisMainMenu>> providers = BasisMenuBase<BasisMainMenu>.Providers;
            AccountServMenuProvider registered = FindProvider(providers);
            if (registered == null)
            {
                instance ??= new AccountServMenuProvider();
                BasisMenuBase<BasisMainMenu>.AddProvider(instance);
                registered = FindProvider(providers);
                if (registered == null)
                {
                    Debug.LogWarning("[AccountServ] Main-menu provider registration was rejected by Basis.");
                    return;
                }
            }
            else
            {
                instance = registered;
            }

            BasisMenuBase<BasisMainMenu> menu = BasisMenuBase<BasisMainMenu>.Instance;
            if (menu == null || HasBoundButton(menu, registered)) return;

            menu.BindProvidersToButtons();
            if (!HasBoundButton(menu, registered))
                Debug.LogWarning("[AccountServ] Provider is registered, but its hotbar button was not created.");
        }

        internal static void ToggleFromShortcut()
        {
            EnsureRegistered("F8");
            AccountServMenuProvider provider = FindProvider(BasisMenuBase<BasisMainMenu>.Providers);
            if (provider == null)
            {
                // The hotbar registration can be suppressed or fail independently. F8 still
                // has a direct provider instance so the account page remains reachable.
                provider = instance ??= new AccountServMenuProvider();
            }

            if (BasisMainMenu.ActivePanelProvider == provider)
            {
                BasisMainMenu.CloseActivePanel();
                return;
            }

            if (BasisMainMenu.Instance == null)
                BasisMainMenu.OpenWithProvider(provider);
            else
                provider.RunAction();
        }

        public override bool Hidden => false;
        public override string Title => "Account";
        public override string IconAddress => AddressableAssets.Sprites.Avatars;
        public override int Order => 800;

        public override void RunAction()
        {
            if (BasisMainMenu.ActivePanelProvider == this ||
                (BasisMainMenu.ActivePanelProvider == null &&
                 BasisMainMenu.Instance != null &&
                 BasisMainMenu.ActiveMenuTitle == Title))
            {
                BasisMainMenu.CloseActivePanel();
                return;
            }

            panel = BasisMainMenu.CreateActiveMenu(
                BasisMenuPanel.PanelData.Standard(Title),
                BasisMenuPanel.PanelStyles.Page,
                this);
            if (panel == null)
            {
                Debug.LogWarning("[AccountServ] Basis did not create the AccountServ panel.");
                return;
            }

            BoundButton?.BindActiveStateToAddressablesInstance(panel);
            panel.OnInstanceReleased += OnPanelClosed;

            feedback = PanelElementDescriptor.CreateNew(
                PanelElementDescriptor.ElementStyles.Group, panel.Descriptor.ContentParent);
            feedback.SetTitle("AccountServ status");
            feedback.SetDescription(AccountServClient.IsAvailable
                ? "Connected to a server advertising AccountServ."
                : "AccountServ is not available on this server, or the connection is not ready.");

            PanelTabGroup tabs = PanelTabGroup.CreateNew(panel.Descriptor.ContentParent, LayoutDirection.Vertical);
            BuildAccountTab(tabs);
            BuildSessionTab(tabs);
            BuildOperatorTab(tabs);

            AccountServClient.ResponseReceived -= OnResponse;
            AccountServClient.ResponseReceived += OnResponse;
        }

        private void BuildAccountTab(PanelTabGroup tabs)
        {
            PanelTabPage page = PanelTabPage.CreateVertical(tabs.Descriptor.ContentParent);
            page.Descriptor.SetTitle("Account");

            PanelElementDescriptor group = PanelElementDescriptor.CreateNew(
                PanelElementDescriptor.ElementStyles.Group, page.Descriptor.ContentParent);
            group.SetTitle("Account credentials");
            group.SetDescription("Register an account for this Basis identity, or identify an existing account.");

            accountNameField = PanelTextField.CreateNewEntry(group.ContentParent);
            accountNameField.Descriptor.SetTitle("Account name");
            accountNameField.Descriptor.SetDescription("The AccountServ account name.");
            accountNameField.SetRequired("Enter an account name.", false);

            passwordField = PanelTextField.CreateNewEntry(group.ContentParent);
            passwordField.Descriptor.SetTitle("Password");
            passwordField.Descriptor.SetDescription("Processed locally. The password itself is never sent.");
            if (passwordField._inputField != null)
                passwordField._inputField.contentType = TMP_InputField.ContentType.Password;
            passwordField.SetValidator(value =>
                string.IsNullOrEmpty(value) ? "Enter your password." :
                value.Length < AccountServClientProtocol.MinimumPasswordLength ? "Password must contain at least 12 characters." :
                value.Length > 256 ? "Password must not exceed 256 characters." : null, false);

            PanelButton register = PanelButton.CreateNew(PanelButton.ButtonStyles.AcceptButton, group.ContentParent);
            register.Descriptor.SetTitle("Register account");
            register.OnClicked += () => SubmitCredentials(true);

            PanelButton identify = PanelButton.CreateNew(PanelButton.ButtonStyles.StandardButton, group.ContentParent);
            identify.Descriptor.SetTitle("Identify");
            identify.OnClicked += () => SubmitCredentials(false);

            tabs.AddTab("Account", () => { }, page);
        }

        private void BuildSessionTab(PanelTabGroup tabs)
        {
            PanelTabPage page = PanelTabPage.CreateVertical(tabs.Descriptor.ContentParent);
            page.Descriptor.SetTitle("Session");

            PanelElementDescriptor group = PanelElementDescriptor.CreateNew(
                PanelElementDescriptor.ElementStyles.Group, page.Descriptor.ContentParent);
            group.SetTitle("Current session");
            group.SetDescription("The session is bound to the authenticated Basis UUID.");

            PanelButton status = PanelButton.CreateNew(group.ContentParent);
            status.Descriptor.SetTitle("Check status");
            status.OnClicked += () => ReportSendResult(AccountServClient.GetStatus());

            PanelButton logout = PanelButton.CreateNew(PanelButton.ButtonStyles.CancelButton, group.ContentParent);
            logout.Descriptor.SetTitle("Log out");
            logout.OnClicked += () => ReportSendResult(AccountServClient.Logout());

            tabs.AddTab("Session", () => { }, page);
        }

        private void BuildOperatorTab(PanelTabGroup tabs)
        {
            PanelTabPage page = PanelTabPage.CreateVertical(tabs.Descriptor.ContentParent);
            page.Descriptor.SetTitle("Operator");

            PanelElementDescriptor group = PanelElementDescriptor.CreateNew(
                PanelElementDescriptor.ElementStyles.Group, page.Descriptor.ContentParent);
            group.SetTitle("Manage operator role");
            group.SetDescription("Only an existing Basis admin can change roles. The server verifies permission.");

            targetUuidField = PanelTextField.CreateNewEntry(group.ContentParent);
            targetUuidField.Descriptor.SetTitle("Target Basis UUID");
            targetUuidField.Descriptor.SetDescription("The DID key UUID of the target account.");
            targetUuidField.SetRequired("Enter a target Basis UUID.", false);

            roleDropdown = PanelDropdown.CreateNewEntry(group.ContentParent);
            roleDropdown.Descriptor.SetTitle("Role");
            roleDropdown.AssignEntries(new List<string> { "admin", "moderator", "none" });
            roleDropdown.SetValueWithoutNotify("moderator");

            PanelButton apply = PanelButton.CreateNew(PanelButton.ButtonStyles.StandardButton, group.ContentParent);
            apply.Descriptor.SetTitle("Apply role");
            apply.OnClicked += () =>
            {
                if (targetUuidField == null || !targetUuidField.Validate()) return;
                int index = roleDropdown != null ? roleDropdown.Index : 1;
                string role = index == 0 ? "admin" : index == 2 ? "none" : "moderator";
                ReportSendResult(AccountServClient.SetRole(targetUuidField.Value, role));
            };

            tabs.AddTab("Operator", () => { }, page);
        }

        private void SubmitCredentials(bool register)
        {
            if (accountNameField == null || passwordField == null ||
                !accountNameField.Validate() || !passwordField.Validate())
                return;

            string accountName = accountNameField.Value;
            string password = passwordField.Value;
            bool sent = register
                ? AccountServClient.Register(accountName, password)
                : AccountServClient.Identify(accountName, password);

            // Clear the UI field immediately after handing the credential to the local
            // derivation routine; AccountServClient retains it only while a challenge is pending.
            passwordField.SetValue(string.Empty);
            ReportSendResult(sent);
        }

        private void OnResponse(string response)
        {
            if (feedback != null)
                feedback.SetDescription(response ?? string.Empty);
        }

        private void ReportSendResult(bool sent)
        {
            if (!sent && feedback != null)
                feedback.SetDescription("Request not sent. Check the connection and AccountServ server plugin.");
        }

        private void OnPanelClosed()
        {
            AccountServClient.ResponseReceived -= OnResponse;
            if (passwordField != null)
                passwordField.SetValue(string.Empty);

            panel = null;
            feedback = null;
            accountNameField = null;
            passwordField = null;
            targetUuidField = null;
            roleDropdown = null;
        }

        public override void OnReleaseEvent() => OnPanelClosed();
    }
}
