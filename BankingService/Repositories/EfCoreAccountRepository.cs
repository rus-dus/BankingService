using BankingService.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BankingService.Services;

public sealed class EfCoreAccountRepository : IAccountRepository
{
    private readonly AccountDbContext _db;
    private readonly ILogger<EfCoreAccountRepository> _logger;

    public EfCoreAccountRepository(AccountDbContext db, ILogger<EfCoreAccountRepository> logger)
    {
        _db     = db     ?? throw new ArgumentNullException(nameof(db));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Account> AddAsync(Account account, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(account);

        try
        {
            _db.Accounts.Add(account);
            await _db.SaveChangesAsync(ct);

            _logger.LogDebug("Account {Id} persisted to database", account.Id);
            return account;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist new account to database");
            throw;
        }
    }

    public async Task<Account> GetByIdAsync(int id, CancellationToken ct = default)
    {
        try
        {
            var account = await _db.Accounts.FindAsync([id], ct);
            if (account is null)
            {
                _logger.LogDebug("Account {Id} not found in database", id);
                throw new KeyNotFoundException($"Account {id} not found.");
            }

            return account;
        }
        catch (Exception ex) when (ex is not KeyNotFoundException)
        {
            _logger.LogError(ex, "Failed to retrieve account {Id} from database", id);
            throw;
        }
    }

    public async Task<Account> UpdateAsync(Account account, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(account);

        try
        {
            _db.Accounts.Update(account);
            await _db.SaveChangesAsync(ct);

            _logger.LogDebug("Account {Id} updated in database", account.Id);
            return account;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning("Optimistic concurrency conflict on account {Id}", account.Id);
            throw new InvalidOperationException(
                "The account was modified by another request. Please retry.", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update account {Id} in database", account.Id);
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

        await using var tx = await _db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, ct);
        try
        {
            var from = await GetByIdAsync(fromId, ct);
            var to   = await GetByIdAsync(toId,   ct);

            operation(from, to);

            _db.Accounts.UpdateRange(from, to);

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _logger.LogWarning(
                    "Optimistic concurrency conflict during transfer {From}→{To}", fromId, toId);
                throw new InvalidOperationException(
                    "A concurrent modification was detected. Please retry the transfer.", ex);
            }

            await tx.CommitAsync(ct);

            _logger.LogDebug(
                "Atomic transfer committed to database: {FromId}→{ToId}", fromId, toId);

            return (from, to);
        }
        catch (Exception ex) when (ex is not KeyNotFoundException and not InvalidOperationException)
        {
            _logger.LogError(ex,
                "Unexpected error during database transfer {FromId}→{ToId}", fromId, toId);
            await tx.RollbackAsync(ct);
            throw;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}