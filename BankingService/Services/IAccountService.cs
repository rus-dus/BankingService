using BankingService.Models;
using BankingService.Models.Requests;

namespace BankingService.Services;

public interface IAccountService
{
    Task<AccountResponse> CreateAccountAsync(CreateAccountRequest request, CancellationToken ct = default);
    Task<AccountResponse> GetAccountAsync(int id, CancellationToken ct = default);
    Task<AccountResponse> SetFrozenAsync(int id, bool freeze, CancellationToken ct = default);
    Task<TransferResponse> TransferAsync(TransferRequest request, CancellationToken ct = default);
}