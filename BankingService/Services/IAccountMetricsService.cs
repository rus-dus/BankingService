namespace BankingService.Services;

public interface IAccountMetricsService
{
    void RecordTransferSuccess(string fromAccountType, string toAccountType);
    void RecordTransferFailure(string reason);
    void RecordAccountCreated(string accountType);
}