using System.Diagnostics.Metrics;

namespace BankingService.Services;

/// <summary>
/// Records business metrics using the System.Diagnostics.Metrics API.
///
/// The <see cref="Meter"/> and its instruments are consumed externally by
/// dotnet-counters, OpenTelemetry exporters, or any MeterListener — not
/// read back through this class.
///
/// Monitor locally with:
///   dotnet-counters monitor -n BankingService --counters BankingService
/// </summary>
public sealed class AccountMetricsService : IAccountMetricsService, IDisposable
{
    public const string MeterName = "BankingService";

    private readonly Meter _meter;
    private readonly Counter<long> _transfersSucceeded;
    private readonly Counter<long> _transfersFailed;
    private readonly Counter<long> _accountsCreated;

    public AccountMetricsService(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);

        _meter = meterFactory.Create(MeterName);

        _transfersSucceeded = _meter.CreateCounter<long>(
            name:        "banking.transfers.succeeded",
            unit:        "{transfer}",
            description: "Number of transfers that completed successfully.");

        _transfersFailed = _meter.CreateCounter<long>(
            name:        "banking.transfers.failed",
            unit:        "{transfer}",
            description: "Number of transfers that failed due to a business rule violation.");

        _accountsCreated = _meter.CreateCounter<long>(
            name:        "banking.accounts.created",
            unit:        "{account}",
            description: "Number of accounts created.");
    }

    /// <param name="fromAccountType">AccountType of the source account (e.g. "Current").</param>
    /// <param name="toAccountType">AccountType of the destination account.</param>
    public void RecordTransferSuccess(string fromAccountType, string toAccountType) =>
        _transfersSucceeded.Add(1,
            new KeyValuePair<string, object?>("from_account_type", fromAccountType),
            new KeyValuePair<string, object?>("to_account_type",   toAccountType));

    /// <param name="reason">Short reason code, e.g. "frozen", "insufficient_balance", "savings_minimum".</param>
    public void RecordTransferFailure(string reason) =>
        _transfersFailed.Add(1,
            new KeyValuePair<string, object?>("reason", reason));

    /// <param name="accountType">AccountType of the created account (e.g. "Savings").</param>
    public void RecordAccountCreated(string accountType) =>
        _accountsCreated.Add(1,
            new KeyValuePair<string, object?>("account_type", accountType));

    public void Dispose() => _meter.Dispose();
}