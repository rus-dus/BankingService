using System.Diagnostics.CodeAnalysis;

namespace BankingService.Models;

/// <summary>Represents a bank account returned by the API.</summary>
/// <param name="Id">Unique account identifier.</param>
/// <param name="OwnerId">Identifier of the account owner.</param>
/// <param name="DisplayName">Human-readable label for this account.</param>
/// <param name="Type">Account type: <c>Current</c> or <c>Savings</c>.</param>
/// <param name="Balance">Current balance.</param>
/// <param name="IsFrozen">Whether the account is frozen. Frozen accounts cannot send funds.</param>
/// <param name="CreatedAt">UTC timestamp when the account was created.</param>
[ExcludeFromCodeCoverage]
public record AccountResponse(
    int Id,
    string OwnerId,
    string DisplayName,
    AccountType Type,
    decimal Balance,
    bool IsFrozen,
    DateTime CreatedAt);

/// <summary>Represents a completed fund transfer between two accounts.</summary>
/// <param name="TransferId">Unique identifier for this transfer.</param>
/// <param name="FromAccountId">ID of the debited account.</param>
/// <param name="ToAccountId">ID of the credited account.</param>
/// <param name="Amount">Amount transferred.</param>
/// <param name="FromBalanceAfter">Balance of the source account after the transfer.</param>
/// <param name="ToBalanceAfter">Balance of the destination account after the transfer.</param>
/// <param name="ExecutedAt">UTC timestamp when the transfer was executed.</param>
[ExcludeFromCodeCoverage]
public record TransferResponse(
    Guid TransferId,
    int FromAccountId,
    int ToAccountId,
    decimal Amount,
    decimal FromBalanceAfter,
    decimal ToBalanceAfter,
    DateTime ExecutedAt);