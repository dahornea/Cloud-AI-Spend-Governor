namespace SpendGovernor.Infrastructure.Persistence;

public sealed class EnvironmentBudget
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public ProjectEntity? Project { get; set; }
    public string Environment { get; set; } = "";
    public decimal? MaxMonthlyCost { get; set; }
    public decimal? MaxMonthlyDelta { get; set; }
    public string Currency { get; set; } = "EUR";
    public decimal? RequireApprovalAbove { get; set; }
    public bool BlockOnBudgetExceeded { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
