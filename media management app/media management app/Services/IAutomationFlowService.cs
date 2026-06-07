using media_management_app.Models;

namespace media_management_app.Services;

public interface IAutomationFlowService
{
    Task<RecipeDryRunResult> DryRunAsync(RecipeRunRequest request, CancellationToken cancellationToken = default);

    Task<RecipeDryRunResult> RunNowAsync(RecipeRunRequest request, CancellationToken cancellationToken = default);
}
