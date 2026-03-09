using System.Diagnostics.Metrics;
using AutoMapper;
using BankingService.Configuration;
using BankingService.Mapping;
using BankingService.Models;
using BankingService.Models.Requests;
using BankingService.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace BankingService.Tests;

/// <summary>
/// Unit tests for AccountService.
///
/// Mock-based tests (majority): IAccountRepository and IAccountMetricsService
/// are substituted so each test covers exactly one unit of behaviour in isolation.
/// Every interaction test carries two assertions:
///   (1) the mock method was called the expected number of times,
///   (2) the mock method was called with the exact expected arguments.
///
/// Concurrency tests live in AccountServiceConcurrencyTests — they require a
/// real InMemoryAccountRepository and cannot be verified through mocks.
/// </summary>
public class AccountServiceTests
{
    // ── Factory helpers ────────────────────────────────────────────────────

    // Pinned timestamp injected via the mocked TimeProvider.
    // Lets ExecutedAt tests assert an exact value instead of a fuzzy range.
    private static readonly DateTimeOffset FixedUtcNow =
        new DateTimeOffset(2026, 3, 9, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Creates an AccountService where every dependency is an NSubstitute mock.
    /// Use <paramref name="mockRepository"/> and <paramref name="mockMetrics"/>
    /// to configure return values and verify calls. Discard the third out param
    /// (<c>out _</c>) when the test does not need to assert on time behaviour.
    /// </summary>
    private static AccountService CreateServiceWithMocks(
        out IAccountRepository     mockRepository,
        out IAccountMetricsService mockMetrics,
        out TimeProvider           mockTimeProvider,
        decimal savingsMinimumBalance = 10m)
    {
        mockRepository = Substitute.For<IAccountRepository>();
        mockMetrics    = Substitute.For<IAccountMetricsService>();

        mockTimeProvider = Substitute.For<TimeProvider>();
        mockTimeProvider.GetUtcNow().Returns(FixedUtcNow);

        var mapper = new MapperConfiguration(cfg =>
            cfg.AddProfile<AccountMappingProfile>()).CreateMapper();

        var settings = Options.Create(new AccountSettings
        {
            SavingsMinimumBalance = savingsMinimumBalance
        });

        return new AccountService(
            mockRepository,
            mockMetrics,
            mapper,
            settings,
            NullLogger<AccountService>.Instance,
            mockTimeProvider);
    }

    // ── Shared request builders ────────────────────────────────────────────

    private static CreateAccountRequest CurrentRequest(decimal balance = 500m) => new()
    {
        OwnerId        = "owner-1",
        DisplayName    = "Current Account",
        Type           = AccountType.Current,
        InitialBalance = balance
    };

    private static CreateAccountRequest SavingsRequest(decimal balance = 200m) => new()
    {
        OwnerId        = "owner-1",
        DisplayName    = "Savings Account",
        Type           = AccountType.Savings,
        InitialBalance = balance
    };

    /// <summary>
    /// Configures ExecuteAtomicTransferAsync to invoke the service-supplied
    /// delegate (so business rules actually run) and return the mutated accounts,
    /// mirroring real repository behaviour.
    /// </summary>
    private static void SetupAtomicTransfer(
        IAccountRepository repo, Account fromAccount, Account toAccount)
    {
        repo.ExecuteAtomicTransferAsync(
                Arg.Any<int>(), Arg.Any<int>(),
                Arg.Any<Action<Account, Account>>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callInfo.Arg<Action<Account, Account>>()(fromAccount, toAccount);
                return Task.FromResult((fromAccount, toAccount));
            });
    }

    // ─────────────────────────────────────────────────────────────────────
    // CreateAccountAsync
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAccount_Current_ReturnsCorrectResponse()
    {
        // Arrange
        var svc = CreateServiceWithMocks(out var repo, out _, out _);
        repo.AddAsync(Arg.Any<Account>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(callInfo.Arg<Account>()));

        // Act
        var result = await svc.CreateAccountAsync(CurrentRequest(100m));

        // Assert
        Assert.Equal(AccountType.Current, result.Type);
        Assert.Equal(100m,                result.Balance);
        Assert.False(result.IsFrozen);
    }

    [Fact]
    public async Task CreateAccount_CallsRepository_WithMappedAccount()
    {
        // Arrange
        var svc = CreateServiceWithMocks(out var repo, out _, out _);
        repo.AddAsync(Arg.Any<Account>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(callInfo.Arg<Account>()));

        // Act
        await svc.CreateAccountAsync(CurrentRequest(300m));

        // Assert — method was called
        await repo.Received(1).AddAsync(Arg.Any<Account>(), Arg.Any<CancellationToken>());

        // Assert — method was called with exact parameters
        await repo.Received(1).AddAsync(
            Arg.Is<Account>(a =>
                a.OwnerId     == "owner-1"         &&
                a.DisplayName == "Current Account" &&
                a.Type        == AccountType.Current &&
                a.Balance     == 300m              &&
                a.IsFrozen    == false),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAccount_CallsMetrics_WithCorrectAccountType()
    {
        // Arrange
        var svc = CreateServiceWithMocks(out var repo, out var metrics, out _);
        repo.AddAsync(Arg.Any<Account>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(callInfo.Arg<Account>()));

        // Act
        await svc.CreateAccountAsync(SavingsRequest(50m));

        // Assert — method was called
        metrics.Received(1).RecordAccountCreated(Arg.Any<string>());

        // Assert — method was called with exact parameters
        metrics.Received(1).RecordAccountCreated("Savings");
    }

    [Fact]
    public async Task CreateAccount_Savings_BelowMinimum_Throws_AndDoesNotCallRepository()
    {
        // Arrange
        var svc = CreateServiceWithMocks(out var repo, out _, out _);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.CreateAccountAsync(SavingsRequest(balance: 5m)));

        // Assert — repository must not be touched when the guard fails
        await repo.DidNotReceive().AddAsync(Arg.Any<Account>(), Arg.Any<CancellationToken>());
    }

    // ─────────────────────────────────────────────────────────────────────
    // GetAccountAsync
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAccount_ExistingId_CallsRepository_AndReturnsResponse()
    {
        // Arrange
        var stored = new Account
        {
            Id = 7, OwnerId = "owner-1", DisplayName = "Test",
            Type = AccountType.Current, Balance = 400m
        };
        var svc = CreateServiceWithMocks(out var repo, out _, out _);
        repo.GetByIdAsync(7, Arg.Any<CancellationToken>()).Returns(Task.FromResult(stored));

        // Act
        var result = await svc.GetAccountAsync(7);

        // Assert — method was called
        await repo.Received(1).GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());

        // Assert — method was called with exact parameters
        await repo.Received(1).GetByIdAsync(7, Arg.Any<CancellationToken>());

        // Assert — full response matches the stored account
        // Assert.Equivalent performs deep structural comparison of all fields at once
        var expected = new AccountResponse(
            stored.Id, stored.OwnerId, stored.DisplayName,
            stored.Type, stored.Balance, stored.IsFrozen, stored.CreatedAt);

        Assert.Equivalent(expected, result);
    }

    [Fact]
    public async Task GetAccount_UnknownId_ThrowsKeyNotFound()
    {
        // Arrange
        var svc = CreateServiceWithMocks(out var repo, out _, out _);
        repo.GetByIdAsync(999, Arg.Any<CancellationToken>())
            .ThrowsAsync(new KeyNotFoundException("Account 999 not found."));

        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(() => svc.GetAccountAsync(999));
    }

    // ─────────────────────────────────────────────────────────────────────
    // SetFrozenAsync
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SetFrozen_True_CallsUpdateWithFrozenAccount()
    {
        // Arrange
        var stored = new Account
        {
            Id = 3, OwnerId = "owner-1", DisplayName = "Test",
            Type = AccountType.Current, Balance = 100m
        };
        var svc = CreateServiceWithMocks(out var repo, out _, out _);
        repo.GetByIdAsync(3, Arg.Any<CancellationToken>()).Returns(Task.FromResult(stored));
        repo.UpdateAsync(Arg.Any<Account>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(callInfo.Arg<Account>()));

        // Act
        var result = await svc.SetFrozenAsync(3, freeze: true);

        // Assert — method was called
        await repo.Received(1).UpdateAsync(Arg.Any<Account>(), Arg.Any<CancellationToken>());

        // Assert — method was called with exact parameters
        await repo.Received(1).UpdateAsync(
            Arg.Is<Account>(a => a.Id == 3 && a.IsFrozen == true),
            Arg.Any<CancellationToken>());

        // Assert — full response matches expected state after freeze
        var expectedFrozen = new AccountResponse(stored.Id, stored.OwnerId, stored.DisplayName,
            stored.Type, stored.Balance, IsFrozen: true, stored.CreatedAt);

        Assert.Equivalent(expectedFrozen, result);
    }

    [Fact]
    public async Task SetFrozen_False_CallsUpdateWithUnfrozenAccount()
    {
        // Arrange
        var stored = new Account
        {
            Id = 4, OwnerId = "owner-1", DisplayName = "Test",
            Type = AccountType.Current, Balance = 100m, IsFrozen = true
        };
        var svc = CreateServiceWithMocks(out var repo, out _, out _);
        repo.GetByIdAsync(4, Arg.Any<CancellationToken>()).Returns(Task.FromResult(stored));
        repo.UpdateAsync(Arg.Any<Account>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(callInfo.Arg<Account>()));

        // Act
        var result = await svc.SetFrozenAsync(4, freeze: false);

        // Assert — method was called
        await repo.Received(1).UpdateAsync(Arg.Any<Account>(), Arg.Any<CancellationToken>());

        // Assert — method was called with exact parameters
        await repo.Received(1).UpdateAsync(
            Arg.Is<Account>(a => a.Id == 4 && a.IsFrozen == false),
            Arg.Any<CancellationToken>());

        // Assert — full response matches expected state after unfreeze
        var expectedUnfrozen = new AccountResponse(stored.Id, stored.OwnerId, stored.DisplayName,
            stored.Type, stored.Balance, IsFrozen: false, stored.CreatedAt);

        Assert.Equivalent(expectedUnfrozen, result);
    }

    [Fact]
    public async Task SetFrozen_UnknownId_ThrowsKeyNotFound()
    {
        // Arrange
        var svc = CreateServiceWithMocks(out var repo, out _, out _);
        repo.GetByIdAsync(999, Arg.Any<CancellationToken>())
            .ThrowsAsync(new KeyNotFoundException("Account 999 not found."));

        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(() => svc.SetFrozenAsync(999, true));
    }

    // ─────────────────────────────────────────────────────────────────────
    // TransferAsync — happy path
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Transfer_Valid_CallsRepository_WithCorrectIds()
    {
        // Arrange
        var fromAccount = new Account { Id = 1, Type = AccountType.Current, Balance = 500m };
        var toAccount   = new Account { Id = 2, Type = AccountType.Current, Balance = 100m };
        var svc         = CreateServiceWithMocks(out var repo, out _, out _);
        SetupAtomicTransfer(repo, fromAccount, toAccount);

        // Act
        await svc.TransferAsync(
            new TransferRequest { FromAccountId = 1, ToAccountId = 2, Amount = 200m });

        // Assert — method was called
        await repo.Received(1).ExecuteAtomicTransferAsync(
            Arg.Any<int>(), Arg.Any<int>(),
            Arg.Any<Action<Account, Account>>(),
            Arg.Any<CancellationToken>());

        // Assert — method was called with exact parameters
        await repo.Received(1).ExecuteAtomicTransferAsync(
            1, 2,
            Arg.Any<Action<Account, Account>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Transfer_Valid_UpdatesBothBalancesInResponse()
    {
        // Arrange
        var fromAccount = new Account { Id = 1, Type = AccountType.Current, Balance = 500m };
        var toAccount   = new Account { Id = 2, Type = AccountType.Current, Balance = 100m };
        var svc         = CreateServiceWithMocks(out var repo, out _, out _);
        SetupAtomicTransfer(repo, fromAccount, toAccount);

        // Act
        var result = await svc.TransferAsync(
            new TransferRequest { FromAccountId = 1, ToAccountId = 2, Amount = 200m });

        // Assert
        Assert.Equal(300m, result.FromBalanceAfter);
        Assert.Equal(300m, result.ToBalanceAfter);
    }

    [Fact]
    public async Task Transfer_Valid_RecordsSuccessMetric_WithAccountTypes()
    {
        // Arrange
        var fromAccount = new Account { Id = 1, Type = AccountType.Current, Balance = 500m };
        var toAccount   = new Account { Id = 2, Type = AccountType.Savings,  Balance = 100m };
        var svc         = CreateServiceWithMocks(out var repo, out var metrics, out _);
        SetupAtomicTransfer(repo, fromAccount, toAccount);

        // Act
        await svc.TransferAsync(
            new TransferRequest { FromAccountId = 1, ToAccountId = 2, Amount = 100m });

        // Assert — method was called
        metrics.Received(1).RecordTransferSuccess(Arg.Any<string>(), Arg.Any<string>());

        // Assert — method was called with exact parameters
        metrics.Received(1).RecordTransferSuccess("Current", "Savings");
    }

    [Fact]
    public async Task Transfer_Valid_ResponseContains_NonEmptyTransferId_AndTimestamp()
    {
        // Arrange
        var fromAccount = new Account { Id = 1, Type = AccountType.Current, Balance = 500m };
        var toAccount   = new Account { Id = 2, Type = AccountType.Current, Balance = 0m };
        var svc         = CreateServiceWithMocks(out var repo, out _, out _);
        SetupAtomicTransfer(repo, fromAccount, toAccount);

        // Act
        var result = await svc.TransferAsync(
            new TransferRequest { FromAccountId = 1, ToAccountId = 2, Amount = 50m });

        // Assert — TransferId is a fresh non-empty Guid; ExecutedAt is the exact
        // value from the mocked TimeProvider, not a live clock range check.
        Assert.NotEqual(Guid.Empty, result.TransferId);
        Assert.Equal(FixedUtcNow.DateTime, result.ExecutedAt);
    }

    // ─────────────────────────────────────────────────────────────────────
    // TransferAsync — business rule violations
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Transfer_FrozenSource_Throws_AndRecordsFailureMetric()
    {
        // Arrange
        var fromAccount = new Account { Id = 1, Type = AccountType.Current, Balance = 500m, IsFrozen = true };
        var toAccount   = new Account { Id = 2, Type = AccountType.Current, Balance = 0m };
        var svc         = CreateServiceWithMocks(out var repo, out var metrics, out _);
        SetupAtomicTransfer(repo, fromAccount, toAccount);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.TransferAsync(new TransferRequest { FromAccountId = 1, ToAccountId = 2, Amount = 100m }));

        // Assert — method was called
        metrics.Received(1).RecordTransferFailure(Arg.Any<string>());

        // Assert — method was called with exact parameters
        metrics.Received(1).RecordTransferFailure("frozen");
    }

    [Fact]
    public async Task Transfer_InsufficientBalance_Throws_AndRecordsFailureMetric()
    {
        // Arrange
        var fromAccount = new Account { Id = 1, Type = AccountType.Current, Balance = 50m };
        var toAccount   = new Account { Id = 2, Type = AccountType.Current, Balance = 0m };
        var svc         = CreateServiceWithMocks(out var repo, out var metrics, out _);
        SetupAtomicTransfer(repo, fromAccount, toAccount);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.TransferAsync(new TransferRequest { FromAccountId = 1, ToAccountId = 2, Amount = 100m }));

        // Assert — method was called
        metrics.Received(1).RecordTransferFailure(Arg.Any<string>());

        // Assert — method was called with exact parameters
        metrics.Received(1).RecordTransferFailure("insufficient_balance");
    }

    [Fact]
    public async Task Transfer_WouldBreachSavingsMinimum_Throws_AndRecordsFailureMetric()
    {
        // Arrange — 110 balance, minimum 10, so max transferable is 100
        var fromAccount = new Account { Id = 1, Type = AccountType.Savings, Balance = 110m };
        var toAccount   = new Account { Id = 2, Type = AccountType.Current, Balance = 0m };
        var svc         = CreateServiceWithMocks(out var repo, out var metrics, out _);
        SetupAtomicTransfer(repo, fromAccount, toAccount);

        // Act & Assert — 101 would leave 9, below the 10 minimum
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.TransferAsync(new TransferRequest { FromAccountId = 1, ToAccountId = 2, Amount = 101m }));

        // Assert — method was called
        metrics.Received(1).RecordTransferFailure(Arg.Any<string>());

        // Assert — method was called with exact parameters
        metrics.Received(1).RecordTransferFailure("savings_minimum");
    }

    [Fact]
    public async Task Transfer_AccountNotFound_Throws_AndRecordsFailureMetric()
    {
        // Arrange
        var svc = CreateServiceWithMocks(out var repo, out var metrics, out _);
        repo.ExecuteAtomicTransferAsync(
                Arg.Any<int>(), Arg.Any<int>(),
                Arg.Any<Action<Account, Account>>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new KeyNotFoundException("Account 999 not found."));

        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            svc.TransferAsync(new TransferRequest { FromAccountId = 999, ToAccountId = 2, Amount = 50m }));

        // Assert — method was called
        metrics.Received(1).RecordTransferFailure(Arg.Any<string>());

        // Assert — method was called with exact parameters
        metrics.Received(1).RecordTransferFailure("account_not_found");
    }

    [Fact]
    public async Task Transfer_SavingsToExactMinimumBalance_Succeeds()
    {
        // Arrange — leaving exactly 10 must be allowed
        var fromAccount = new Account { Id = 1, Type = AccountType.Savings, Balance = 110m };
        var toAccount   = new Account { Id = 2, Type = AccountType.Current, Balance = 0m };
        var svc         = CreateServiceWithMocks(out var repo, out _, out _);
        SetupAtomicTransfer(repo, fromAccount, toAccount);

        // Act
        var result = await svc.TransferAsync(
            new TransferRequest { FromAccountId = 1, ToAccountId = 2, Amount = 100m });

        // Assert
        Assert.Equal(10m, result.FromBalanceAfter);
    }

    [Fact]
    public async Task Transfer_CurrentAccountDrainedToZero_Succeeds()
    {
        // Arrange — current accounts have no minimum balance rule
        var fromAccount = new Account { Id = 1, Type = AccountType.Current, Balance = 100m };
        var toAccount   = new Account { Id = 2, Type = AccountType.Current, Balance = 0m };
        var svc         = CreateServiceWithMocks(out var repo, out _, out _);
        SetupAtomicTransfer(repo, fromAccount, toAccount);

        // Act
        var result = await svc.TransferAsync(
            new TransferRequest { FromAccountId = 1, ToAccountId = 2, Amount = 100m });

        // Assert
        Assert.Equal(0m, result.FromBalanceAfter);
    }

    // ─────────────────────────────────────────────────────────────────────
    // IOptions<AccountSettings>
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SavingsMinimumBalance_CustomValue_ExactlyAtMinimum_Succeeds()
    {
        // Arrange — custom minimum of 50
        var fromAccount = new Account { Id = 1, Type = AccountType.Savings, Balance = 150m };
        var toAccount   = new Account { Id = 2, Type = AccountType.Current, Balance = 0m };
        var svc         = CreateServiceWithMocks(out var repo, out _, out _, savingsMinimumBalance: 50m);
        SetupAtomicTransfer(repo, fromAccount, toAccount);

        // Act — leaving exactly 50 must succeed
        var result = await svc.TransferAsync(
            new TransferRequest { FromAccountId = 1, ToAccountId = 2, Amount = 100m });

        // Assert
        Assert.Equal(50m, result.FromBalanceAfter);
    }

    [Fact]
    public async Task SavingsMinimumBalance_CustomValue_OnePennyBelow_Throws()
    {
        // Arrange
        var fromAccount = new Account { Id = 1, Type = AccountType.Savings, Balance = 150m };
        var toAccount   = new Account { Id = 2, Type = AccountType.Current, Balance = 0m };
        var svc         = CreateServiceWithMocks(out var repo, out _, out _, savingsMinimumBalance: 50m);
        SetupAtomicTransfer(repo, fromAccount, toAccount);

        // Act & Assert — 101 would leave 49, below the 50 custom minimum
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.TransferAsync(new TransferRequest { FromAccountId = 1, ToAccountId = 2, Amount = 101m }));
    }

}