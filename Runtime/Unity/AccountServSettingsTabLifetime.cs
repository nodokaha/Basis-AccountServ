using System.Collections.Generic;
using Basis.BasisUI;
using TMPro;
using UnityEngine;

namespace BasisAccountServ
{
    /// <summary>
    /// Owns the controls and response subscription for one realized Settings tab.
    /// The component is destroyed with its page, so the response event cannot retain stale UI.
    /// </summary>
    public sealed class AccountServSettingsTabLifetime : MonoBehaviour
    {
        private PanelElementDescriptor feedback;
        private PanelTextField accountNameField;
        private PanelTextField passwordField;
        private PanelTextField targetUuidField;
        private PanelDropdown roleDropdown;

        internal void Build(PanelTabPage page)
        {
            RectTransform content = page.Descriptor.ContentParent;

            feedback = PanelElementDescriptor.CreateNew(
                PanelElementDescriptor.ElementStyles.Group, content);
            feedback.SetTitle("AccountServ status");
            feedback.SetDescription(AccountServClient.IsAvailable
                ? "Connected to a server advertising AccountServ."
                : "AccountServ is not available on this server, or the connection is not ready.");

            BuildAccountSection(content);
            BuildSessionSection(content);
            BuildOperatorSection(content);

            AccountServClient.ResponseReceived -= OnResponse;
            AccountServClient.ResponseReceived += OnResponse;
        }

        private void BuildAccountSection(RectTransform content)
        {
            PanelElementDescriptor group = PanelElementDescriptor.CreateNew(
                PanelElementDescriptor.ElementStyles.Group, content);
            group.SetTitle("Account");
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
        }

        private void BuildSessionSection(RectTransform content)
        {
            PanelElementDescriptor group = PanelElementDescriptor.CreateNew(
                PanelElementDescriptor.ElementStyles.Group, content);
            group.SetTitle("Session");
            group.SetDescription("The session is bound to the authenticated Basis UUID.");

            PanelButton status = PanelButton.CreateNew(group.ContentParent);
            status.Descriptor.SetTitle("Check status");
            status.OnClicked += () => ReportSendResult(AccountServClient.GetStatus());

            PanelButton logout = PanelButton.CreateNew(PanelButton.ButtonStyles.CancelButton, group.ContentParent);
            logout.Descriptor.SetTitle("Log out");
            logout.OnClicked += () => ReportSendResult(AccountServClient.Logout());
        }

        private void BuildOperatorSection(RectTransform content)
        {
            PanelElementDescriptor group = PanelElementDescriptor.CreateNew(
                PanelElementDescriptor.ElementStyles.Group, content);
            group.SetTitle("Operator");
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

        private void OnDestroy()
        {
            AccountServClient.ResponseReceived -= OnResponse;
            if (passwordField != null)
                passwordField.SetValue(string.Empty);

            feedback = null;
            accountNameField = null;
            passwordField = null;
            targetUuidField = null;
            roleDropdown = null;
        }
    }
}
