using System.Diagnostics.CodeAnalysis;

namespace BankingService.Models.Requests;

/// <summary>Request body for creating a new account.</summary>
[ExcludeFromCodeCoverage]
public record CreateAccountRequest
{
    /// <summary>Identifier of the account owner (e.g. a user ID from your auth system).</summary>
    /// <example>user-42</example>
    public string OwnerId { get; init; } = string.Empty;

    /// <summary>Human-readable label for this account.</summary>
    /// <example>My Current Account</example>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Type of account to open. Must be <c>Current</c> or <c>Savings</c>.</summary>
    /// <example>Current</example>
    public AccountType Type { get; init; }

    /// <summary>
    /// Opening balance. Must be non-negative.
    /// Savings accounts must open with at least the configured minimum balance.
    /// </summary>
    /// <example>500.00</example>
    public decimal InitialBalance { get; init; }
}

/// <summary>Request body for transferring funds between two accounts.</summary>
[ExcludeFromCodeCoverage]
public record TransferRequest
{
    /// <summary>ID of the account to debit.</summary>
    /// <example>1</example>
    public int FromAccountId { get; init; }

    /// <summary>ID of the account to credit.</summary>
    /// <example>2</example>
    public int ToAccountId { get; init; }

    /// <summary>Amount to transfer. Must be greater than zero.</summary>
    /// <example>150.00</example>
    public decimal Amount { get; init; }
}

/// <summary>Request body for freezing or unfreezing an account.</summary>
[ExcludeFromCodeCoverage]
public record FreezeRequest
{
    /// <summary><c>true</c> to freeze the account; <c>false</c> to unfreeze it.</summary>
    /// <example>true</example>
    public bool Freeze { get; init; }
}