using AutoMapper;
using BankingService.Models;
using BankingService.Models.Requests;

namespace BankingService.Mapping;

public sealed class AccountMappingProfile : Profile
{
    public AccountMappingProfile()
    {
        // CreateAccountRequest → Account
        // Id and CreatedAt are set by the Account constructor defaults,
        // so we ignore them here and let the entity own its own identity.
        CreateMap<CreateAccountRequest, Account>()
            .ForMember(dest => dest.Id,         opt => opt.Ignore())
            .ForMember(dest => dest.CreatedAt,  opt => opt.Ignore())
            .ForMember(dest => dest.IsFrozen,   opt => opt.Ignore())
            .ForMember(dest => dest.RowVersion, opt => opt.Ignore())
            .ForMember(dest => dest.Balance,    opt => opt.MapFrom(src => src.InitialBalance));

        // Account → AccountResponse (outbound DTO)
        CreateMap<Account, AccountResponse>();
    }
}