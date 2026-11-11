using System;
using System.Collections.Generic;
using Basis.BasisUI;
using NUnit.Framework;

namespace BasisAccountServ.Tests
{
    public sealed class AccountServMenuProviderTests
    {
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
        public void RegisterIfMissing_InvokesRegistration_WhenProviderIsAbsent()
        {
            var providers = new List<BasisMenuActionProvider<BasisMainMenu>>();
            int registrationCalls = 0;

            bool registered = AccountServMenuProvider.RegisterIfMissing(
                providers,
                () => registrationCalls++);

            Assert.That(registered, Is.True);
            Assert.That(registrationCalls, Is.EqualTo(1));
        }

        [Test]
        public void RegisterIfMissing_DoesNotRegisterDuplicateProvider()
        {
            var providers = new List<BasisMenuActionProvider<BasisMainMenu>>
            {
                new AccountServMenuProvider()
            };
            int registrationCalls = 0;

            bool registered = AccountServMenuProvider.RegisterIfMissing(
                providers,
                () => registrationCalls++);

            Assert.That(registered, Is.False);
            Assert.That(registrationCalls, Is.Zero);
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
