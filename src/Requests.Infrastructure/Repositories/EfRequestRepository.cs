using Microsoft.EntityFrameworkCore;
using Requests.Application.DTOs;
using Requests.Application.Interfaces;
using Requests.Domain.Entities;
using Requests.Infrastructure.Persistence;

namespace Requests.Infrastructure.Repositories;

public sealed class EfRequestRepository : IRequestRepository
{
    private readonly AppDbContext _db;
    public EfRequestRepository(AppDbContext db) => _db = db;

    public Task AddAsync(Request request, CancellationToken ct) => _db.Requests.AddAsync(request, ct).AsTask();

    public Task<Request?> GetByIdAsync(long id, CancellationToken ct)
        => _db.Requests.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<Request?> GetByExternalIdAsync(string externalId, CancellationToken ct)
        => _db.Requests.AsNoTracking().FirstOrDefaultAsync(x => x.ExternalId == externalId, ct);

    public async Task<(IReadOnlyList<Request> Items, int Total)> ListAsync(RequestFilter f, CancellationToken ct)
    {
        var q = _db.Requests.AsNoTracking();
        if (f.Status is { } s) q = q.Where(x => x.Status == s);

        var total = await q.CountAsync(ct);

        // allowlist: only CreatedAt, ASC/DESC
        var descending = !string.Equals(f.SortDirection, "ASC", StringComparison.OrdinalIgnoreCase);
        q = descending ? q.OrderByDescending(x => x.CreatedAt) : q.OrderBy(x => x.CreatedAt);

        var items = await q
            .Skip((f.Page - 1) * f.PageSize)
            .Take(f.PageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}