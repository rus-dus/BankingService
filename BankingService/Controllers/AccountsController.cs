using Asp.Versioning;
using BankingService.Models;
using BankingService.Models.Requests;
using BankingService.Services;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Mvc;

namespace BankingService.Controllers;

/// <summary>Manages bank accounts and fund transfers.</summary>
[ApiController]
[ApiVersion(1)]
[Route("api/v{version:apiVersion}/accounts")]
[Produces("application/json")]
public class AccountsController : ControllerBase
{
    private readonly IAccountService                    _service;
    private readonly ILogger<AccountsController>        _logger;
    private readonly IValidator<CreateAccountRequest>   _createValidator;
    private readonly IValidator<TransferRequest>        _transferValidator;

    public AccountsController(
        IAccountService                  service,
        ILogger<AccountsController>      logger,
        IValidator<CreateAccountRequest> createValidator,
        IValidator<TransferRequest>      transferValidator)
    {
        _service           = service           ?? throw new ArgumentNullException(nameof(service));
        _logger            = logger            ?? throw new ArgumentNullException(nameof(logger));
        _createValidator   = createValidator   ?? throw new ArgumentNullException(nameof(createValidator));
        _transferValidator = transferValidator ?? throw new ArgumentNullException(nameof(transferValidator));
    }

    /// <summary>Creates a new Current or Savings account.</summary>
    /// <param name="request">Account creation details.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The newly created account.</returns>
    /// <response code="201">Account created successfully.</response>
    /// <response code="400">Validation failed or business rule violated.</response>
    [HttpPost]
    [ProducesResponseType(typeof(AccountResponse),  StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails),   StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromBody] CreateAccountRequest request,
        CancellationToken ct)
    {
        var validation = await _createValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            validation.AddToModelState(ModelState, prefix: null);
            return ValidationProblem();
        }

        try
        {
            var account = await _service.CreateAccountAsync(request, ct);
            return CreatedAtAction(nameof(GetById), new { version = "1", id = account.Id }, account);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("Create account failed: {Message}", ex.Message);
            return BadRequest(ProblemDetailsFor(ex.Message));
        }
    }

    /// <summary>Retrieves an account by its ID.</summary>
    /// <param name="id">The account ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The account with the given ID.</returns>
    /// <response code="200">Account found.</response>
    /// <response code="404">No account with the given ID exists.</response>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(AccountResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails),  StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        try
        {
            var account = await _service.GetAccountAsync(id, ct);
            return Ok(account);
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning("Account {Id} not found: {Message}", id, ex.Message);
            return NotFound(ProblemDetailsFor(ex.Message));
        }
    }

    /// <summary>Freezes or unfreezes an account.</summary>
    /// <param name="id">The account ID.</param>
    /// <param name="request">Freeze state to apply.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated account.</returns>
    /// <response code="200">Freeze state updated.</response>
    /// <response code="400">Validation failed.</response>
    /// <response code="404">No account with the given ID exists.</response>
    [HttpPatch("{id:int}/freeze")]
    [ProducesResponseType(typeof(AccountResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails),  StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails),  StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetFrozen(
        int id,
        [FromBody] FreezeRequest request,
        CancellationToken ct)
    {
        try
        {
            var account = await _service.SetFrozenAsync(id, request.Freeze, ct);
            return Ok(account);
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning("Account {Id} not found for freeze: {Message}", id, ex.Message);
            return NotFound(ProblemDetailsFor(ex.Message));
        }
    }

    /// <summary>Transfers funds between two accounts.</summary>
    /// <param name="request">Transfer details including source, destination, and amount.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Transfer receipt including balances after the operation.</returns>
    /// <response code="200">Transfer completed successfully.</response>
    /// <response code="400">Validation failed or business rule violation (frozen, insufficient funds, etc.).</response>
    /// <response code="404">Source or destination account not found.</response>
    [HttpPost("transfers")]
    [ProducesResponseType(typeof(TransferResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails),   StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails),   StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Transfer(
        [FromBody] TransferRequest request,
        CancellationToken ct)
    {
        var validation = await _transferValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            validation.AddToModelState(ModelState, prefix: null);
            return ValidationProblem();
        }

        try
        {
            var result = await _service.TransferAsync(request, ct);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning("Transfer failed — account not found: {Message}", ex.Message);
            return NotFound(ProblemDetailsFor(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("Transfer failed — business rule: {Message}", ex.Message);
            return BadRequest(ProblemDetailsFor(ex.Message));
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static ProblemDetails ProblemDetailsFor(string detail) => new()
    {
        Detail = detail
    };
}