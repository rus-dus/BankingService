using System.Diagnostics.Metrics;
using BankingService.Services;

namespace BankingService.Tests;

/// <summary>
/// Tests for AccountMetricsService using MeterListener — the standard
/// in-process mechanism for reading System.Diagnostics.Metrics values.
/// The service itself cannot be mocked here because it IS the unit under test.
/// </summary>
public class AccountMetricsServiceTests : IDisposable
{
    private readonly IMeterFactory _meterFactory;
    private readonly AccountMetricsService _svc;

    // Counters incremented by MeterListener callbacks.
    private long _succeededCount;
    private long _failedCount;
    private long _createdCount;

    // Last tags recorded per instrument.
    private readonly List<KeyValuePair<string, object?>> _lastSuccessTags = new();
    private readonly List<KeyValuePair<string, object?>> _lastFailureTags = new();
    private readonly List<KeyValuePair<string, object?>> _lastCreatedTags = new();

    private readonly MeterListener _listener;

    public AccountMetricsServiceTests()
    {
        _meterFactory = new TestMeterFactory();
        _svc          = new AccountMetricsService(_meterFactory);

        _listener = new MeterListener();

        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == AccountMetricsService.MeterName)
                listener.EnableMeasurementEvents(instrument);
        };

        _listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
        {
            switch (instrument.Name)
            {
                case "banking.transfers.succeeded":
                    Interlocked.Add(ref _succeededCount, measurement);
                    _lastSuccessTags.Clear();
                    _lastSuccessTags.AddRange(tags.ToArray());
                    break;

                case "banking.transfers.failed":
                    Interlocked.Add(ref _failedCount, measurement);
                    _lastFailureTags.Clear();
                    _lastFailureTags.AddRange(tags.ToArray());
                    break;

                case "banking.accounts.created":
                    Interlocked.Add(ref _createdCount, measurement);
                    _lastCreatedTags.Clear();
                    _lastCreatedTags.AddRange(tags.ToArray());
                    break;
            }
        });

        _listener.Start();
    }

    public void Dispose()
    {
        _listener.Dispose();
        _svc.Dispose();
        _meterFactory.Dispose();
    }

    // ── RecordTransferSuccess ──────────────────────────────────────────────

    [Fact]
    public void RecordTransferSuccess_IncrementsCounter()
    {
        // Arrange — fresh service, counter is 0

        // Act
        _svc.RecordTransferSuccess("Current", "Savings");

        // Assert
        Assert.Equal(1, _succeededCount);
    }

    [Fact]
    public void RecordTransferSuccess_RecordsTags_WithCorrectAccountTypes()
    {
        // Arrange — fresh service

        // Act
        _svc.RecordTransferSuccess("Current", "Savings");

        // Assert — from_account_type tag
        Assert.Contains(_lastSuccessTags,
            t => t.Key == "from_account_type" && t.Value!.Equals("Current"));

        // Assert — to_account_type tag
        Assert.Contains(_lastSuccessTags,
            t => t.Key == "to_account_type" && t.Value!.Equals("Savings"));
    }

    [Fact]
    public void RecordTransferSuccess_MultipleCallsAccumulate()
    {
        // Arrange — fresh service

        // Act
        _svc.RecordTransferSuccess("Current", "Current");
        _svc.RecordTransferSuccess("Current", "Current");
        _svc.RecordTransferSuccess("Savings", "Current");

        // Assert
        Assert.Equal(3, _succeededCount);
    }

    // ── RecordTransferFailure ──────────────────────────────────────────────

    [Fact]
    public void RecordTransferFailure_IncrementsCounter()
    {
        // Arrange — fresh service

        // Act
        _svc.RecordTransferFailure("frozen");

        // Assert
        Assert.Equal(1, _failedCount);
    }

    [Fact]
    public void RecordTransferFailure_RecordsReasonTag()
    {
        // Arrange — fresh service

        // Act
        _svc.RecordTransferFailure("insufficient_balance");

        // Assert
        Assert.Contains(_lastFailureTags,
            t => t.Key == "reason" && t.Value!.Equals("insufficient_balance"));
    }

    [Theory]
    [InlineData("frozen")]
    [InlineData("insufficient_balance")]
    [InlineData("savings_minimum")]
    [InlineData("account_not_found")]
    [InlineData("unknown")]
    public void RecordTransferFailure_AllKnownReasons_CorrectlyTagged(string reason)
    {
        // Arrange — fresh service

        // Act
        _svc.RecordTransferFailure(reason);

        // Assert
        Assert.Contains(_lastFailureTags, t => t.Key == "reason" && t.Value!.Equals(reason));
    }

    // ── RecordAccountCreated ───────────────────────────────────────────────

    [Fact]
    public void RecordAccountCreated_IncrementsCounter()
    {
        // Arrange — fresh service

        // Act
        _svc.RecordAccountCreated("Current");

        // Assert
        Assert.Equal(1, _createdCount);
    }

    [Fact]
    public void RecordAccountCreated_RecordsAccountTypeTag()
    {
        // Arrange — fresh service

        // Act
        _svc.RecordAccountCreated("Savings");

        // Assert
        Assert.Contains(_lastCreatedTags,
            t => t.Key == "account_type" && t.Value!.Equals("Savings"));
    }

    // ── Constructor ────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullMeterFactory_Throws()
    {
        // Arrange — intentionally null

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new AccountMetricsService(null!));
    }
}

/// <summary>
/// Minimal IMeterFactory for unit tests.
/// Creates a real Meter so MeterListener can intercept measurements.
/// </summary>
file sealed class TestMeterFactory : IMeterFactory
{
    private readonly List<Meter> _meters = new();

    public Meter Create(MeterOptions options)
    {
        var meter = new Meter(options.Name, options.Version);
        _meters.Add(meter);
        return meter;
    }

    public void Dispose() => _meters.ForEach(m => m.Dispose());
}