using System.Diagnostics.Metrics;
using AutoMapper;
using BankingService.Configuration;
using BankingService.Controllers;
using BankingService.Middleware;
using BankingService.Models.Requests;
using BankingService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using FluentValidation;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace BankingService.Tests;

/// <summary>
/// Verifies that every constructor with null guards throws
/// ArgumentNullException for each nullable parameter, and that
/// the exception carries the correct parameter name.
/// One test per parameter keeps failures pinpointed.
/// </summary>
public class ConstructorNullGuardTests
{
    // ── Shared valid stubs ─────────────────────────────────────────────────

    private static IAccountRepository ValidRepository =>
        Substitute.For<IAccountRepository>();

    private static IAccountMetricsService ValidMetrics =>
        Substitute.For<IAccountMetricsService>();

    private static IMapper ValidMapper =>
        Substitute.For<IMapper>();

    private static IOptions<AccountSettings> ValidSettings =>
        Options.Create(new AccountSettings());

    private static ILogger<AccountService> ValidLogger =>
        NullLogger<AccountService>.Instance;

    // TimeProvider is an abstract class — TimeProvider.System is the real
    // production singleton; it is safe to use in guard tests.
    private static TimeProvider ValidTimeProvider =>
        TimeProvider.System;

    private static IAccountService ValidService =>
        Substitute.For<IAccountService>();

    private static IValidator<CreateAccountRequest> ValidCreateValidator =>
        Substitute.For<IValidator<CreateAccountRequest>>();

    private static IValidator<FreezeRequest> ValidFreezeValidator =>
        Substitute.For<IValidator<FreezeRequest>>();

    private static IValidator<TransferRequest> ValidTransferValidator =>
        Substitute.For<IValidator<TransferRequest>>();

    // NSubstitute cannot mock AccountDbContext because it has no parameterless
    // constructor. Use a real in-memory DbContext instead — the guard fires
    // before the context is ever used, so this is safe and correct.
    private static AccountDbContext ValidDbContext =>
        new AccountDbContext(
            new DbContextOptionsBuilder<AccountDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);

    private static RequestDelegate ValidNext => _ => Task.CompletedTask;

    // ── AccountMetricsService ──────────────────────────────────────────────

    [Fact]
    public void AccountMetricsService_NullMeterFactory_Throws()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new AccountMetricsService(null!));

        Assert.Equal("meterFactory", ex.ParamName);
    }

    // ── AccountService ─────────────────────────────────────────────────────

    [Fact]
    public void AccountService_NullRepository_Throws()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new AccountService(
                null!,
                ValidMetrics,
                ValidMapper,
                ValidSettings,
                ValidLogger,
                ValidTimeProvider));

        Assert.Equal("repository", ex.ParamName);
    }

    [Fact]
    public void AccountService_NullMetrics_Throws()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new AccountService(
                ValidRepository,
                null!,
                ValidMapper,
                ValidSettings,
                ValidLogger,
                ValidTimeProvider));

        Assert.Equal("metrics", ex.ParamName);
    }

    [Fact]
    public void AccountService_NullMapper_Throws()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new AccountService(
                ValidRepository,
                ValidMetrics,
                null!,
                ValidSettings,
                ValidLogger,
                ValidTimeProvider));

        Assert.Equal("mapper", ex.ParamName);
    }

    [Fact]
    public void AccountService_NullSettings_Throws()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new AccountService(
                ValidRepository,
                ValidMetrics,
                ValidMapper,
                null!,
                ValidLogger,
                ValidTimeProvider));

        Assert.Equal("settings", ex.ParamName);
    }

    [Fact]
    public void AccountService_NullLogger_Throws()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new AccountService(
                ValidRepository,
                ValidMetrics,
                ValidMapper,
                ValidSettings,
                null!,
                ValidTimeProvider));

        Assert.Equal("logger", ex.ParamName);
    }

    [Fact]
    public void AccountService_NullTimeProvider_Throws()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new AccountService(
                ValidRepository,
                ValidMetrics,
                ValidMapper,
                ValidSettings,
                ValidLogger,
                null!));

        Assert.Equal("timeProvider", ex.ParamName);
    }

    // ── InMemoryAccountRepository ──────────────────────────────────────────

    [Fact]
    public void InMemoryAccountRepository_NullLogger_Throws()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new InMemoryAccountRepository(null!));

        Assert.Equal("logger", ex.ParamName);
    }

    // ── EfCoreAccountRepository ────────────────────────────────────────────

    [Fact]
    public void EfCoreAccountRepository_NullDbContext_Throws()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new EfCoreAccountRepository(
                null!,
                NullLogger<EfCoreAccountRepository>.Instance));

        Assert.Equal("db", ex.ParamName);
    }

    [Fact]
    public void EfCoreAccountRepository_NullLogger_Throws()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new EfCoreAccountRepository(
                ValidDbContext,
                null!));

        Assert.Equal("logger", ex.ParamName);
    }

    // ── AccountsController ─────────────────────────────────────────────────

    [Fact]
    public void AccountsController_NullService_Throws()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new AccountsController(
                null!,
                NullLogger<AccountsController>.Instance,
                ValidCreateValidator,
                ValidTransferValidator));

        Assert.Equal("service", ex.ParamName);
    }

    [Fact]
    public void AccountsController_NullLogger_Throws()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new AccountsController(
                ValidService,
                null!,
                ValidCreateValidator,
                ValidTransferValidator));

        Assert.Equal("logger", ex.ParamName);
    }

    [Fact]
    public void AccountsController_NullCreateValidator_Throws()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new AccountsController(
                ValidService,
                NullLogger<AccountsController>.Instance,
                null!,
                ValidTransferValidator));

        Assert.Equal("createValidator", ex.ParamName);
    }

    [Fact]
    public void AccountsController_NullTransferValidator_Throws()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new AccountsController(
                ValidService,
                NullLogger<AccountsController>.Instance,
                ValidCreateValidator,
                null!));

        Assert.Equal("transferValidator", ex.ParamName);
    }

    // ── ExceptionHandlingMiddleware ────────────────────────────────────────

    [Fact]
    public void ExceptionHandlingMiddleware_NullNext_Throws()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new ExceptionHandlingMiddleware(
                null!,
                NullLogger<ExceptionHandlingMiddleware>.Instance));

        Assert.Equal("next", ex.ParamName);
    }

    [Fact]
    public void ExceptionHandlingMiddleware_NullLogger_Throws()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new ExceptionHandlingMiddleware(
                ValidNext,
                null!));

        Assert.Equal("logger", ex.ParamName);
    }
}

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