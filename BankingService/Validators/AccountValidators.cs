using BankingService.Models;
using BankingService.Models.Requests;
using FluentValidation;

namespace BankingService.Validators;

public sealed class CreateAccountRequestValidator : AbstractValidator<CreateAccountRequest>
{
    public CreateAccountRequestValidator()
    {
        RuleFor(x => x.OwnerId)
            .NotEmpty()
            .WithMessage("OwnerId is required.")
            .MaximumLength(100)
            .WithMessage("OwnerId must not exceed 100 characters.");

        RuleFor(x => x.DisplayName)
            .NotEmpty()
            .WithMessage("DisplayName is required.")
            .MaximumLength(200)
            .WithMessage("DisplayName must not exceed 200 characters.");

        RuleFor(x => x.Type)
            .IsInEnum()
            .WithMessage("Type must be either 'Current' or 'Savings'.");

        RuleFor(x => x.InitialBalance)
            .GreaterThanOrEqualTo(0)
            .WithMessage("InitialBalance must be non-negative.");
    }
}

public sealed class TransferRequestValidator : AbstractValidator<TransferRequest>
{
    public TransferRequestValidator()
    {
        RuleFor(x => x.FromAccountId)
            .GreaterThan(0)
            .WithMessage("FromAccountId must be a positive integer.");

        RuleFor(x => x.ToAccountId)
            .GreaterThan(0)
            .WithMessage("ToAccountId must be a positive integer.");

        RuleFor(x => x.Amount)
            .GreaterThan(0)
            .WithMessage("Amount must be greater than zero.");

        RuleFor(x => x)
            .Must(x => x.FromAccountId != x.ToAccountId)
            .WithName("ToAccountId")
            .WithMessage("Source and destination accounts must differ.");
    }
}