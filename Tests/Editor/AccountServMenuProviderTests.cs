using System.Collections.Generic;
using Basis.BasisUI;
using NUnit.Framework;

namespace BasisAccountServ.Tests
{
    public sealed class AccountServMenuProviderTests
    {
        [SetUp]
        public void SetUp()
        {
            ResetProviderState();
        }

        [TearDown]
        public void TearDown()
        {
            ResetProviderState();
        }

        private static void ResetProviderState()
        {
            BasisMenuBase<BasisMainMenu>.AllowProvider(typeof(AccountServMenuProvider));

            // Use the public API so an already-created menu is rebound as well.
            List<BasisMenuActionProvider<BasisMainMenu>> existing =
                BasisMenuBase<BasisMainMenu>.Providers.FindAll(
                    provider => provider is AccountServMenuProvider);

            foreach (BasisMenuActionProvider<BasisMainMenu> provider in existing)
                BasisMenuBase<BasisMainMenu>.RemoveProvider(provider);
        }

        [Test]
        public void ContainsProvider_ReturnsFalse_WhenProviderListIsEmpty()
        {
            var providers = new List<BasisMenuActionProvider<BasisMainMenu>>();

            Assert.That(AccountServMenuProvider.ContainsProvider(providers), Is.False);
        }

        [Test]
        public void ContainsProvider_ReturnsTrue_WhenAccountServProviderIsPresent()
        {
            var providers = new List<BasisMenuActionProvider<BasisMainMenu>>
            {
                new AccountServMenuProvider()
            };

            Assert.That(AccountServMenuProvider.ContainsProvider(providers), Is.True);
        }

        [Test]
        public void Register_AddsProviderToBasisMainMenu()
        {
            AccountServMenuProvider.Register();

            Assert.That(
                AccountServMenuProvider.ContainsProvider(BasisMenuBase<BasisMainMenu>.Providers),
                Is.True);
        }

        [Test]
        public void Register_DoesNotAddDuplicateProvider()
        {
            AccountServMenuProvider.Register();
            int countAfterFirstRegistration =
                BasisMenuBase<BasisMainMenu>.Providers.FindAll(
                    provider => provider is AccountServMenuProvider).Count;

            AccountServMenuProvider.Register();

            int countAfterSecondRegistration =
                BasisMenuBase<BasisMainMenu>.Providers.FindAll(
                    provider => provider is AccountServMenuProvider).Count;

            Assert.That(countAfterFirstRegistration, Is.EqualTo(1));
            Assert.That(countAfterSecondRegistration, Is.EqualTo(1));
        }

        [Test]
        public void EnsureRegistered_AddsProviderOnce_WhenCalledRepeatedly()
        {
            AccountServMenuProvider.EnsureRegistered("test-1");
            AccountServMenuProvider.EnsureRegistered("test-2");
            AccountServMenuProvider.EnsureRegistered("test-3");
            int count = BasisMenuBase<BasisMainMenu>.Providers.FindAll(
                provider => provider is AccountServMenuProvider).Count;
            Assert.That(count, Is.EqualTo(1));
        }

        [Test]
        public void FindProvider_ReturnsRegisteredProvider_AndNullWhenAbsent()
        {
            var empty = new List<BasisMenuActionProvider<BasisMainMenu>>();
            Assert.That(AccountServMenuProvider.FindProvider(empty), Is.Null);
            AccountServMenuProvider.Register();
            Assert.That(AccountServMenuProvider.FindProvider(BasisMenuBase<BasisMainMenu>.Providers), Is.Not.Null);
        }

        [Test]
        public void HasBoundButton_ReturnsFalse_WhenMenuOrProviderIsMissing()
        {
            Assert.That(AccountServMenuProvider.HasBoundButton(null, null), Is.False);
            Assert.That(AccountServMenuProvider.HasBoundButton(null, new AccountServMenuProvider()), Is.False);
        }

        [Test]
        public void Register_ReAddsProvider_AfterItWasRemovedFromTheSharedList()
        {
            AccountServMenuProvider.Register();
            ResetProviderState();
            AccountServMenuProvider.Register();
            Assert.That(AccountServMenuProvider.ContainsProvider(BasisMenuBase<BasisMainMenu>.Providers), Is.True);
        }

        [Test]
        public void Provider_UsesVisibleMenuEntryWithConfiguredIconAndOrder()
        {
            var provider = new AccountServMenuProvider();

            Assert.That(provider.Hidden, Is.False);
            Assert.That(provider.Title, Is.EqualTo("Account"));
            Assert.That(provider.IconAddress, Is.Not.Null.And.Not.Empty);
            Assert.That(provider.Order, Is.EqualTo(800));
        }
    }
}
