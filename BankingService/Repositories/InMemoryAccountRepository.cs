using System.Collections.Concurrent;
using BankingService.Models;
using Microsoft.Extensions.Logging;

namespace BankingService.Services;

public sealed class InMemoryAccountRepository : IAccountRepository
{
    private readonly ConcurrentDictionary<int, Account> _accounts = new();
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _locks = new();
    private readonly ILogger<InMemoryAccountRepository> _logger;
    private int _nextId;

    public InMemoryAccountRepository(ILogger<InMemoryAccountRepository> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<Account> AddAsync(Account account, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(account);

        try
        {
            account.Id = Interlocked.Increment(ref _nextId);
            _accounts[account.Id] = account;
            _locks[account.Id]    = new SemaphoreSlim(1, 1);

            _logger.LogDebug("Account {Id} added to in-memory store", account.Id);
            return Task.FromResult(account);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add account to in-memory store");
            throw;
        }
    }

    public Task<Account> GetByIdAsync(int id, CancellationToken ct = default)
    {
        try
        {
            return Task.FromResult(Resolve(id));
        }
        catch (KeyNotFoundException)
        {
            _logger.LogDebug("Account {Id} not found in in-memory store", id);
            throw;
        }
    }

    public async Task<Account> UpdateAsync(Account account, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(account);

        try
        {
            var sem = ResolveLock(account.Id);
            await sem.WaitAsync(ct);
            try
            {
                _accounts[account.Id] = account;
                _logger.LogDebug("Account {Id} updated in in-memory store", account.Id);
                return account;
            }
            finally
            {
                sem.Release();
            }
        }
        catch (Exception ex) when (ex is not KeyNotFoundException)
        {
            _logger.LogError(ex, "Failed to update account {Id} in in-memory store", account.Id);
            throw;
        }
    }

    public async Task<(Account From, Account To)> ExecuteAtomicTransferAsync(
        int fromId,
        int toId,
        Action<Account, Account> operation,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        try
        {
            var (first, second) = fromId < toId ? (fromId, toId) : (toId, fromId);

            await ResolveLock(first).WaitAsync(ct);
            try
            {
                await ResolveLock(second).WaitAsync(ct);
                try
                {
                    var from = Resolve(fromId);
                    var to   = Resolve(toId);

                    operation(from, to);

                    _logger.LogDebug(
                        "Atomic transfer executed in-memory: {FromId}→{ToId}",
                        fromId, toId);

                    return (from, to);
                }
                finally
                {
                    ResolveLock(second).Release();
                }
            }
            finally
            {
                ResolveLock(first).Release();
            }
        }
        catch (Exception ex) when (ex is not KeyNotFoundException and not InvalidOperationException)
        {
            _logger.LogError(ex,
                "Unexpected error during atomic transfer {FromId}→{ToId}", fromId, toId);
            throw;
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private Account Resolve(int id) =>
        _accounts.TryGetValue(id, out var a) ? a
            : throw new KeyNotFoundException($"Account {id} not found.");

    private SemaphoreSlim ResolveLock(int id) =>
        _locks.TryGetValue(id, out var s) ? s
            : throw new KeyNotFoundException($"Lock for account {id} not found.");
}