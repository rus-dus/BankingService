using System.Diagnostics.CodeAnalysis;

namespace BankingService.Configuration;

[ExcludeFromCodeCoverage]
public sealed class AccountSettings
{
    /// <summary>Configuration section name in appsettings.json.</summary>
    public const string SectionName = "AccountSettings";

    /// <summary>
    /// Minimum balance that must remain in a Savings account at all times.
    /// Defaults to 10 if not specified in configuration.
    /// </summary>
    public decimal SavingsMinimumBalance { get; init; } = 10m;
}