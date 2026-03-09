using System.Diagnostics.CodeAnalysis;

namespace BankingService.Models;

public enum AccountType
{
    Current,
    Savings
}

[ExcludeFromCodeCoverage]
public class Account
{
    public int Id { get; set; }
    public string OwnerId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public AccountType Type { get; init; }
    public decimal Balance { get; set; }
    public bool IsFrozen { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Concurrency token used by EF Core for optimistic concurrency.
    /// Ignored by the in-memory repository.
    /// </summary>
    public byte[]? RowVersion { get; set; }
}