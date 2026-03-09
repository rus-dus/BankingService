using BankingService.Models;

namespace BankingService.Services;

public interface IAccountRepository
{
    Task<Account> AddAsync(Account account, CancellationToken ct = default);
    Task<Account> GetByIdAsync(int id, CancellationToken ct = default);
    Task<Account> UpdateAsync(Account account, CancellationToken ct = default);

    /// <summary>
    /// Fetches both accounts and executes <paramref name="operation"/> on them
    /// inside an atomic boundary.
    /// </summary>
    Task<(Account From, Account To)> ExecuteAtomicTransferAsync(
        int fromId,
        int toId,
        Action<Account, Account> operation,
        CancellationToken ct = default);
}