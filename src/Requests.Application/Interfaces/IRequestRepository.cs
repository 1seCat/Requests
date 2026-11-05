using Requests.Application.DTOs;
using Requests.Domain.Entities;
namespace Requests.Application.Interfaces;
public interface IRequestRepository
{
    Task AddAsync(Request request, CancellationToken ct);
    Task<Request?> GetByIdAsync(long id, CancellationToken ct);
    Task<Request?> GetByExternalIdAsync(string externalId, CancellationToken ct);
    Task<(IReadOnlyList<Request> Items, int Total)> ListAsync(RequestFilter filter, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}