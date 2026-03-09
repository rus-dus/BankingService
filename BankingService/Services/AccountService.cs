using AutoMapper;
using BankingService.Configuration;
using BankingService.Models;
using BankingService.Models.Requests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BankingService.Services;

public sealed class AccountService : IAccountService
{
    private readonly IAccountRepository        _repository;
    private readonly IAccountMetricsService    _metrics;
    private readonly IMapper                   _mapper;
    private readonly ILogger<AccountService>   _logger;
    private readonly AccountSettings           _settings;
    private readonly TimeProvider              _timeProvider;

    public AccountService(
        IAccountRepository        repository,
        IAccountMetricsService    metrics,
        IMapper                   mapper,
        IOptions<AccountSettings> settings,
        ILogger<AccountService>   logger,
        TimeProvider              timeProvider)
    {
        _repository   = repository      ?? throw new ArgumentNullException(nameof(repository));
        _metrics      = metrics         ?? throw new ArgumentNullException(nameof(metrics));
        _mapper       = mapper          ?? throw new ArgumentNullException(nameof(mapper));
        _settings     = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
        _logger       = logger          ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider    ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<AccountResponse> CreateAccountAsync(
        CreateAccountRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            if (request.Type == AccountType.Savings &&
                request.InitialBalance < _settings.SavingsMinimumBalance)
            {
                throw new InvalidOperationException(
                    $"Savings accounts must open with at least {_settings.SavingsMinimumBalance:C}.");
            }

            var account = _mapper.Map<Account>(request);
            await _repository.AddAsync(account, ct);

            _metrics.RecordAccountCreated(account.Type.ToString());

            _logger.LogInformation(
                "Account created. Id={Id} Owner={Owner} Type={Type} Balance={Balance}",
                account.Id, account.OwnerId, account.Type, account.Balance);

            return _mapper.Map<AccountResponse>(account);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            _logger.LogError(ex,
                "Unexpected error creating account for Owner={Owner}", request.OwnerId);
            throw;
        }
    }

    public async Task<AccountResponse> GetAccountAsync(int id, CancellationToken ct = default)
    {
        try
        {
            var account = await _repository.GetByIdAsync(id, ct);
            return _mapper.Map<AccountResponse>(account);
        }
        catch (KeyNotFoundException)
        {
            _logger.LogWarning("Account {Id} not found", id);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error retrieving account {Id}", id);
            throw;
        }
    }

    public async Task<AccountResponse> SetFrozenAsync(
        int id, bool freeze, CancellationToken ct = default)
    {
        try
        {
            var account = await _repository.GetByIdAsync(id, ct);
            account.IsFrozen = freeze;
            await _repository.UpdateAsync(account, ct);

            _logger.LogInformation("Account {Id} {Action}.", id, freeze ? "frozen" : "unfrozen");
            return _mapper.Map<AccountResponse>(account);
        }
        catch (KeyNotFoundException)
        {
            _logger.LogWarning("Account {Id} not found for freeze operation", id);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error freezing account {Id}", id);
            throw;
        }
    }

    public async Task<TransferResponse> TransferAsync(
        TransferRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var (from, to) = await _repository.ExecuteAtomicTransferAsync(
                request.FromAccountId,
                request.ToAccountId,
                (from, to) =>
                {
                    ValidateTransfer(from, to, request.Amount);
                    from.Balance -= request.Amount;
                    to.Balance   += request.Amount;
                },
                ct);

            var response = new TransferResponse(
                TransferId:       Guid.NewGuid(),
                FromAccountId:    from.Id,
                ToAccountId:      to.Id,
                Amount:           request.Amount,
                FromBalanceAfter: from.Balance,
                ToBalanceAfter:   to.Balance,
                ExecutedAt:       _timeProvider.GetUtcNow().DateTime);

            _metrics.RecordTransferSuccess(from.Type.ToString(), to.Type.ToString());

            _logger.LogInformation(
                "Transfer {TransferId}: {Amount} from {From} (bal={FromBal}) to {To} (bal={ToBal})",
                response.TransferId, request.Amount,
                from.Id, from.Balance, to.Id, to.Balance);

            return response;
        }
        catch (InvalidOperationException ex)
        {
            _metrics.RecordTransferFailure(ExtractFailureReason(ex.Message));
            _logger.LogWarning(
                "Transfer rejected {From} to {To}: {Reason}",
                request.FromAccountId, request.ToAccountId, ex.Message);
            throw;
        }
        catch (KeyNotFoundException ex)
        {
            _metrics.RecordTransferFailure("account_not_found");
            _logger.LogWarning(
                "Transfer failed - account not found {From} to {To}: {Message}",
                request.FromAccountId, request.ToAccountId, ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            _metrics.RecordTransferFailure("unknown");
            _logger.LogError(ex,
                "Unexpected error during transfer {From} to {To}",
                request.FromAccountId, request.ToAccountId);
            throw;
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static string ExtractFailureReason(string message)
    {
        if (message.Contains("frozen"))          return "frozen";
        if (message.Contains("Insufficient"))    return "insufficient_balance";
        if (message.Contains("minimum balance")) return "savings_minimum";
        return "unknown";
    }

    private void ValidateTransfer(Account from, Account to, decimal amount)
    {
        if (from.IsFrozen)
            throw new InvalidOperationException(
                $"Account {from.Id} is frozen and cannot send funds.");

        if (amount > from.Balance)
            throw new InvalidOperationException(
                $"Insufficient balance. Available: {from.Balance:C}, requested: {amount:C}.");

        if (from.Type == AccountType.Savings &&
            from.Balance - amount < _settings.SavingsMinimumBalance)
        {
            throw new InvalidOperationException(
                $"Transfer would breach the savings minimum balance of {_settings.SavingsMinimumBalance:C}. " +
                $"Available to transfer: {from.Balance - _settings.SavingsMinimumBalance:C}.");
        }
    }
}