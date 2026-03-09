using AutoMapper;
using BankingService.Mapping;
using BankingService.Models;
using BankingService.Models.Requests;

namespace BankingService.Tests;

public class MappingTests
{
    private readonly IMapper _mapper;

    public MappingTests()
    {
        _mapper = new MapperConfiguration(cfg =>
            cfg.AddProfile<AccountMappingProfile>()).CreateMapper();
    }

    // ── Configuration ──────────────────────────────────────────────────────

    [Fact]
    public void AllMappings_AreConfiguredCorrectly()
    {
        // Arrange — mapper is configured in constructor

        // Act & Assert — AutoMapper throws if any destination properties are unmapped
        _mapper.ConfigurationProvider.AssertConfigurationIsValid();
    }

    // ── CreateAccountRequest → Account ────────────────────────────────────

    [Fact]
    public void CreateAccountRequest_MapsTo_Account_AllProperties()
    {
        // Arrange
        var request = new CreateAccountRequest
        {
            OwnerId        = "user-42",
            DisplayName    = "Test Account",
            Type           = AccountType.Savings,
            InitialBalance = 250m
        };

        // Act
        var account = _mapper.Map<Account>(request);

        // Assert — Assert.Equivalent(strict: false) compares only the fields present in
        // the anonymous expected object; runtime-assigned fields (Id, CreatedAt) are
        // excluded from expected so they do not cause the assertion to fail.
        Assert.Equivalent(
            new
            {
                request.OwnerId,
                request.DisplayName,
                request.Type,
                Balance     = request.InitialBalance    // InitialBalance → Balance rename
            },
            account,
            strict: false);
    }

    [Fact]
    public void CreateAccountRequest_MapsTo_Account_EntityDefaults_ArePreserved()
    {
        // Arrange
        var before = DateTime.UtcNow;

        // Act
        var account = _mapper.Map<Account>(new CreateAccountRequest
        {
            OwnerId     = "user-1",
            DisplayName = "Account",
            Type        = AccountType.Current
        });

        // Assert — mapper must not overwrite fields owned by the persistence layer
        Assert.Equal(0,     account.Id);            // assigned by repository on save
        Assert.True(account.CreatedAt >= before);   // entity sets its own timestamp
        Assert.False(account.IsFrozen);             // defaults to unfrozen
        Assert.Null(account.RowVersion);            // assigned only after first SaveChanges
    }

    // ── Account → AccountResponse ──────────────────────────────────────────

    [Fact]
    public void Account_MapsTo_AccountResponse_AllProperties()
    {
        // Arrange
        var account = new Account
        {
            Id          = 5,
            OwnerId     = "user-99",
            DisplayName = "My Current",
            Type        = AccountType.Current,
            Balance     = 500m,
            IsFrozen    = true,
        };

        // Act
        var response = _mapper.Map<AccountResponse>(account);

        // Assert — Assert.Equivalent performs a deep structural comparison, verifying
        // every property in one statement instead of individual Assert.Equal calls.
        var expected = new AccountResponse(
            account.Id,
            account.OwnerId,
            account.DisplayName,
            account.Type,
            account.Balance,
            account.IsFrozen,
            account.CreatedAt);

        Assert.Equivalent(expected, response);
    }

    [Fact]
    public void Account_MapsTo_AccountResponse_RowVersion_IsNotExposed()
    {
        // Arrange — RowVersion is an internal persistence detail

        // Act — inspect the public API of the response DTO
        var property = typeof(AccountResponse).GetProperty("RowVersion");

        // Assert — it must not appear on the outbound DTO
        Assert.Null(property);
    }
}