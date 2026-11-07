using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Accounting.Contracts;

public sealed record ExpenseDto(int Id, DateOnly Date, string Category, decimal Amount, string? Note);

public sealed record AddExpenseRequest(string Category, decimal Amount, DateOnly Date, string? Note);

public static class ExpenseCategories
{
    public static readonly string[] All =
    {
        "Pakan", "Bibit / DOC", "Obat & vaksin", "Listrik", "Air", "Tenaga kerja", "Kemasan",
        "Sewa", "Transportasi", "Biaya operasional", "Biaya lain"
    };
}

public interface IExpenseService
{
    Task<Result<int>> AddAsync(AddExpenseRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<ExpenseDto>> ListAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
    Task<Result> DeleteAsync(int id, CancellationToken ct = default);
}
