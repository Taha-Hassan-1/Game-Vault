using GameVault.Application.Metadata;

namespace GameVault.Infrastructure.Rawg;

/// <summary>
/// Sits inside the retry handler so every attempt, retries included, is counted against the monthly budget.
/// Throwing (not returning a fake response) keeps the Polly retry strategy from retrying an exhausted budget.
/// </summary>
public sealed class BudgetHandler : DelegatingHandler
{
    private readonly IRequestBudget _budget;
    public BudgetHandler(IRequestBudget budget) => _budget = budget;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (!await _budget.TryConsumeAsync(ct)) throw new BudgetExceededException(RequestBudget.Provider);
        return await base.SendAsync(request, ct);
    }
}
