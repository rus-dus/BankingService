using BankingService.Models;
using BankingService.Models.Requests;
using BankingService.Validators;
using FluentValidation.TestHelper;

namespace BankingService.Tests;

public class ValidatorTests
{
    // ── CreateAccountRequestValidator ──────────────────────────────────────

    public class CreateAccountRequestValidatorTests
    {
        private readonly CreateAccountRequestValidator _validator = new();

        [Fact]
        public void Valid_CurrentAccount_PassesValidation()
        {
            // Arrange
            var request = new CreateAccountRequest
            {
                OwnerId        = "user-1",
                DisplayName    = "My Account",
                Type           = AccountType.Current,
                InitialBalance = 100m
            };

            // Act
            var result = _validator.TestValidate(request);

            // Assert
            result.ShouldNotHaveAnyValidationErrors();
        }

        [Fact]
        public void Valid_SavingsAccount_ZeroBalance_PassesValidation()
        {
            // Arrange — balance below minimum is a business rule enforced in AccountService,
            //            NOT a format rule; the validator must not reject it here.
            var request = new CreateAccountRequest
            {
                OwnerId        = "user-1",
                DisplayName    = "Savings",
                Type           = AccountType.Savings,
                InitialBalance = 0m
            };

            // Act
            var result = _validator.TestValidate(request);

            // Assert
            result.ShouldNotHaveAnyValidationErrors();
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void Empty_OwnerId_FailsValidation(string ownerId)
        {
            // Arrange
            var request = new CreateAccountRequest
            {
                OwnerId        = ownerId,
                DisplayName    = "My Account",
                Type           = AccountType.Current,
                InitialBalance = 0m
            };

            // Act
            var result = _validator.TestValidate(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.OwnerId);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void Empty_DisplayName_FailsValidation(string displayName)
        {
            // Arrange
            var request = new CreateAccountRequest
            {
                OwnerId        = "user-1",
                DisplayName    = displayName,
                Type           = AccountType.Current,
                InitialBalance = 0m
            };

            // Act
            var result = _validator.TestValidate(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.DisplayName);
        }

        [Fact]
        public void OwnerId_ExceedingMaxLength_FailsValidation()
        {
            // Arrange
            var request = new CreateAccountRequest
            {
                OwnerId        = new string('x', 101),
                DisplayName    = "My Account",
                Type           = AccountType.Current,
                InitialBalance = 0m
            };

            // Act
            var result = _validator.TestValidate(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.OwnerId);
        }

        [Fact]
        public void DisplayName_ExceedingMaxLength_FailsValidation()
        {
            // Arrange
            var request = new CreateAccountRequest
            {
                OwnerId        = "user-1",
                DisplayName    = new string('x', 201),
                Type           = AccountType.Current,
                InitialBalance = 0m
            };

            // Act
            var result = _validator.TestValidate(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.DisplayName);
        }

        [Fact]
        public void InvalidAccountType_FailsValidation()
        {
            // Arrange
            var request = new CreateAccountRequest
            {
                OwnerId        = "user-1",
                DisplayName    = "My Account",
                Type           = (AccountType)99,
                InitialBalance = 0m
            };

            // Act
            var result = _validator.TestValidate(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.Type);
        }

        [Fact]
        public void NegativeInitialBalance_FailsValidation()
        {
            // Arrange
            var request = new CreateAccountRequest
            {
                OwnerId        = "user-1",
                DisplayName    = "My Account",
                Type           = AccountType.Current,
                InitialBalance = -1m
            };

            // Act
            var result = _validator.TestValidate(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.InitialBalance);
        }
    }

    // ── TransferRequestValidator ───────────────────────────────────────────

    public class TransferRequestValidatorTests
    {
        private readonly TransferRequestValidator _validator = new();

        private const int AccountA = 1;
        private const int AccountB = 2;

        [Fact]
        public void Valid_Transfer_PassesValidation()
        {
            // Arrange
            var request = new TransferRequest
            {
                FromAccountId = AccountA,
                ToAccountId   = AccountB,
                Amount        = 50m
            };

            // Act
            var result = _validator.TestValidate(request);

            // Assert
            result.ShouldNotHaveAnyValidationErrors();
        }

        [Fact]
        public void ZeroFromAccountId_FailsValidation()
        {
            // Arrange
            var request = new TransferRequest
            {
                FromAccountId = 0,
                ToAccountId   = AccountB,
                Amount        = 50m
            };

            // Act
            var result = _validator.TestValidate(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.FromAccountId);
        }

        [Fact]
        public void ZeroToAccountId_FailsValidation()
        {
            // Arrange
            var request = new TransferRequest
            {
                FromAccountId = AccountA,
                ToAccountId   = 0,
                Amount        = 50m
            };

            // Act
            var result = _validator.TestValidate(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.ToAccountId);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(-100)]
        public void ZeroOrNegativeAmount_FailsValidation(decimal amount)
        {
            // Arrange
            var request = new TransferRequest
            {
                FromAccountId = AccountA,
                ToAccountId   = AccountB,
                Amount        = amount
            };

            // Act
            var result = _validator.TestValidate(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.Amount);
        }

        [Fact]
        public void SameSourceAndDestination_FailsValidation()
        {
            // Arrange
            var request = new TransferRequest
            {
                FromAccountId = AccountA,
                ToAccountId   = AccountA,
                Amount        = 50m
            };

            // Act
            var result = _validator.TestValidate(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.ToAccountId);
        }
    }
}