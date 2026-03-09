using System.Diagnostics.Metrics;
using AutoMapper;
using BankingService.Configuration;
using BankingService.Mapping;
using BankingService.Models;
using BankingService.Models.Requests;
using BankingService.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace BankingService.Tests;

/// <summary>
/// Concurrency tests for AccountService.
///
/// These tests require a real InMemoryAccountRepository because threading
/// behaviour — lock ordering, balance conservation, deadlock prevention —
/// cannot be meaningfully verified through mocks. Every test spins up its own
/// isolated service instance so runs are independent of each other.
/// </summary>
public class AccountServiceConcurrencyTests
{
    // ── Factory ────────────────────────────────────────────────────────────

    private static AccountService CreateService(decimal savingsMinimumBalance = 10m)
    {
        var mapper = new MapperConfiguration(cfg =>
            cfg.AddProfile<AccountMappingProfile>()).CreateMapper();

        var settings = Options.Create(new AccountSettings
        {
            SavingsMinimumBalance = savingsMinimumBalance
        });

        return new AccountService(
            new InMemoryAccountRepository(NullLogger<InMemoryAccountRepository>.Instance),
            new AccountMetricsService(new TestMeterFactory()),
            mapper,
            settings,
            NullLogger<AccountService>.Instance,
            TimeProvider.System);
    }

    // ── Shared request builders ────────────────────────────────────────────

    private static CreateAccountRequest CurrentRequest(decimal balance) => new()
    {
        OwnerId        = "owner-1",
        DisplayName    = "Current Account",
        Type           = AccountType.Current,
        InitialBalance = balance
    };

    // ─────────────────────────────────────────────────────────────────────
    // Tests
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SimultaneousTransfers_TotalBalanceIsConserved()
    {
        // Arrange
        var svc  = CreateService();
        var from = await svc.CreateAccountAsync(CurrentRequest(1000m));
        var to   = await svc.CreateAccountAsync(CurrentRequest(0m));

        // Act — 100 concurrent transfers of 10 each
        var results = await Task.WhenAll(
            Enumerable.Range(0, 100).Select(_ =>
                svc.TransferAsync(new TransferRequest
                {
                    FromAccountId = from.Id, ToAccountId = to.Id, Amount = 10m
                }).ContinueWith(t => !t.IsFaulted)));

        var succeeded = results.Count(r => r);
        var fromFinal = (await svc.GetAccountAsync(from.Id)).Balance;
        var toFinal   = (await svc.GetAccountAsync(to.Id)).Balance;

        // Assert
        Assert.Equal(1000m,                   fromFinal + toFinal);
        Assert.Equal(1000m - succeeded * 10m, fromFinal);
    }

    [Fact]
    public async Task OppositeDirectionTransfers_NoDeadlock()
    {
        // Arrange — A→B and B→A in parallel validates the ordered-lock pattern:
        // locks are always acquired in ascending account-ID order, so two
        // threads approaching the same pair from opposite directions cannot
        // cycle-wait on each other.
        var svc = CreateService();
        var a   = await svc.CreateAccountAsync(CurrentRequest(500m));
        var b   = await svc.CreateAccountAsync(CurrentRequest(500m));

        // Act
        await Task.WhenAll(
            Enumerable.Range(0, 50).SelectMany(_ => new[]
            {
                svc.TransferAsync(new TransferRequest { FromAccountId = a.Id, ToAccountId = b.Id, Amount = 1m }),
                svc.TransferAsync(new TransferRequest { FromAccountId = b.Id, ToAccountId = a.Id, Amount = 1m })
            }).Select(t => t.ContinueWith(_ => { })));  // swallow individual failures

        var aFinal = (await svc.GetAccountAsync(a.Id)).Balance;
        var bFinal = (await svc.GetAccountAsync(b.Id)).Balance;

        // Assert — test completes (no deadlock) and total is conserved
        Assert.Equal(1000m, aFinal + bFinal);
    }

    [Fact]
    public async Task FreezeInterleavedWithTransfers_BalanceAlwaysConsistent()
    {
        // Arrange
        var svc  = CreateService();
        var from = await svc.CreateAccountAsync(CurrentRequest(500m));
        var to   = await svc.CreateAccountAsync(CurrentRequest(0m));

        // Act — freeze/unfreeze toggles racing against concurrent transfers
        var toggleTask = Task.Run(async () =>
        {
            for (int i = 0; i < 20; i++)
            {
                await svc.SetFrozenAsync(from.Id, i % 2 == 0);
                await Task.Delay(1);
            }
            await svc.SetFrozenAsync(from.Id, false);  // always end unfrozen
        });

        var movedAmounts = await Task.WhenAll(
            Enumerable.Range(0, 50).Select(_ =>
                svc.TransferAsync(new TransferRequest
                {
                    FromAccountId = from.Id, ToAccountId = to.Id, Amount = 5m
                }).ContinueWith(t => t.IsFaulted ? 0m : 5m)));

        await toggleTask;

        var moved     = movedAmounts.Sum();
        var fromFinal = (await svc.GetAccountAsync(from.Id)).Balance;
        var toFinal   = (await svc.GetAccountAsync(to.Id)).Balance;

        // Assert — every pound that left 'from' arrived in 'to', no money lost
        Assert.Equal(500m,         fromFinal + toFinal);
        Assert.Equal(500m - moved, fromFinal);
        Assert.Equal(moved,        toFinal);
    }
}

file sealed class TestMeterFactory : IMeterFactory
{
    private readonly List<Meter> _meters = new();
    public Meter Create(MeterOptions options)
    {
        var meter = new Meter(options.Name, options.Version);
        _meters.Add(meter);
        return meter;
    }
    public void Dispose() => _meters.ForEach(m => m.Dispose());
}