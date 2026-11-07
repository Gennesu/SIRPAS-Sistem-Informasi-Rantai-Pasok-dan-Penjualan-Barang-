using Microsoft.EntityFrameworkCore;
using Platform.Web.Data;
using Platform.Web.Modules.Accounting.Contracts;
using Platform.Web.Modules.Accounting.Domain;
using Platform.Web.Modules.Audit.Contracts;
using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Accounting.Services;

public sealed class ExpenseService : IExpenseService
{
    private const decimal MaxMoney = 1_000_000_000_000m;

    private readonly AppDbContext _db;
    private readonly IBusinessContext _context;
    private readonly IAuditLogService _audit;
    private readonly TimeProvider _time;

    public ExpenseService(AppDbContext db, IBusinessContext context, IAuditLogService audit, TimeProvider time)
    {
        _db = db;
        _context = context;
        _audit = audit;
        _time = time;
    }

    public async Task<Result<int>> AddAsync(AddExpenseRequest request, CancellationToken ct = default)
    {
        var businessId = _context.BusinessId;
        if (!_context.IsAuthenticated || _context.IsAdmin || businessId is null)
        {
            return Result<int>.Fail(ErrorKind.Forbidden, "Hanya akun usaha yang dapat mencatat pengeluaran.");
        }

        var category = ExpenseCategories.All.FirstOrDefault(
            c => string.Equals(c, request.Category?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (category is null)
        {
            return Result<int>.Fail(ErrorKind.Validation, "Kategori pengeluaran tidak valid.");
        }

        if (request.Amount <= 0 || request.Amount > MaxMoney)
        {
            return Result<int>.Fail(ErrorKind.Validation, "Jumlah pengeluaran harus lebih dari 0.");
        }

        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        if (note is { Length: > 200 })
        {
            return Result<int>.Fail(ErrorKind.Validation, "Catatan maksimal 200 karakter.");
        }

        var expense = new Expense
        {
            BusinessId = businessId.Value,
            Date = request.Date,
            Category = category,
            Amount = Money.Round(request.Amount),
            Note = note,
            CreatedAt = _time.GetUtcNow().UtcDateTime,
            CreatedByUserId = _context.UserId
        };
        _db.Expenses.Add(expense);
        await _db.SaveChangesAsync(ct);

        _audit.Record("EXPENSE_ADDED", "Expense", expense.Id.ToString(), $"{category}; {expense.Amount}");
        await _db.SaveChangesAsync(ct);
        return Result<int>.Ok(expense.Id);
    }

    public async Task<IReadOnlyList<ExpenseDto>> ListAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var rows = await _db.Expenses.AsNoTracking().ToListAsync(ct);
        return rows
            .Where(e => e.Date >= from && e.Date <= to)
            .OrderByDescending(e => e.Date)
            .ThenByDescending(e => e.Id)
            .Select(e => new ExpenseDto(e.Id, e.Date, e.Category, e.Amount, e.Note))
            .ToList();
    }

    public async Task<Result> DeleteAsync(int id, CancellationToken ct = default)
    {
        if (!_context.IsAuthenticated || _context.IsAdmin || _context.BusinessId is null)
        {
            return Result.Fail(ErrorKind.Forbidden, "Hanya akun usaha yang dapat menghapus pengeluaran.");
        }

        var expense = await _db.Expenses.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (expense is null)
        {
            return Result.Fail(ErrorKind.NotFound, "Pengeluaran tidak ditemukan.");
        }

        _db.Expenses.Remove(expense);
        _audit.Record("EXPENSE_DELETED", "Expense", id.ToString(), $"{expense.Category}; {expense.Amount}");
        await _db.SaveChangesAsync(ct);
        return Result.Ok();
    }
}
