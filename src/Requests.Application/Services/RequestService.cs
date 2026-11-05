using System.Text.Json;
using Requests.Application.DTOs;
using Requests.Application.Interfaces;
using Requests.Domain.Entities;
using Requests.Domain.Enums;
using Requests.Domain.Events;
using Requests.Domain.Exceptions;

namespace Requests.Application.Services;

public sealed class RequestService
{
    private readonly IRequestRepository _repo;
    private readonly IOutboxWriter _outbox;
    private readonly IUnitOfWork _uow;

    public RequestService(IRequestRepository repo, IOutboxWriter outbox, IUnitOfWork uow)
    { _repo = repo; _outbox = outbox; _uow = uow; }

    public async Task<RequestDto> CreateAsync(CreateRequestRequest dto, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var entity = Request.Create(dto.ExternalId, dto.Title, dto.Description, now);

        // duplicate check (fast-fail); DB unique index is the real guard
        if (await _repo.GetByExternalIdAsync(entity.ExternalId, ct) is not null)
            throw new DuplicateExternalIdException(entity.ExternalId);

        var evt = new RequestCreatedDomainEvent(entity.Id == 0 ? 0 : entity.Id,
            entity.ExternalId, entity.Title, entity.Status.ToString());
        var eventId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        await _uow.ExecuteInTransactionAsync(async () =>
        {
            await _repo.AddAsync(entity, ct);
            await _uow.SaveChangesAsync(ct); // assigns Id

            var payload = new RequestCreatedDomainEvent(entity.Id, entity.ExternalId, entity.Title, entity.Status.ToString());
            await _outbox.EnqueueAsync("RequestCreated", payload, eventId, correlationId, ct);
            await _uow.SaveChangesAsync(ct);
        }, ct);

        return ToDto(entity);
    }

    public async Task<RequestDto?> GetAsync(long id, CancellationToken ct)
    {
        var e = await _repo.GetByIdAsync(id, ct);
        return e is null ? null : ToDto(e);
    }

    public async Task<PagedResult<RequestDto>> ListAsync(RequestFilter filter, CancellationToken ct)
    {
        var (items, total) = await _repo.ListAsync(filter, ct);
        return new PagedResult<RequestDto>(
            items.Select(ToDto).ToList(), total, filter.Page, filter.PageSize);
    }

    public async Task<RequestDto> UpdateStatusAsync(long id, UpdateStatusRequest dto, CancellationToken ct)
    {
        if (!Enum.TryParse<RequestStatus>(dto.Status, ignoreCase: true, out var newStatus))
            throw new ArgumentException("Invalid status value.");

        var entity = await _repo.GetByIdAsync(id, ct)
                     ?? throw new RequestNotFoundException(id);

        if (entity.RowVersion is null || Convert.ToBase64String(entity.RowVersion) != dto.RowVersion)
            throw new ConcurrencyConflictException();

        var oldStatus = entity.Status;
        entity.ChangeStatus(newStatus, DateTime.UtcNow);

        var eventId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        await _uow.ExecuteInTransactionAsync(async () =>
        {
            // RowVersion check happens in SaveChanges via EF concurrency token
            var payload = new RequestStatusChangedDomainEvent(entity.Id, oldStatus.ToString(), newStatus.ToString());
            await _outbox.EnqueueAsync("RequestStatusChanged", payload, eventId, correlationId, ct);
            await _uow.SaveChangesAsync(ct);
        }, ct);

        return ToDto(entity);
    }

    private static RequestDto ToDto(Request e) => new(
        e.Id, e.ExternalId, e.Title, e.Description, e.Status.ToString(),
        e.CreatedAt, e.UpdatedAt, Convert.ToBase64String(e.RowVersion ?? Array.Empty<byte>()));
}

public sealed class DuplicateExternalIdException(string externalId)
    : Exception($"Request with ExternalId '{externalId}' already exists.");
public sealed class RequestNotFoundException(long id) : Exception($"Request {id} not found.");
public sealed class ConcurrencyConflictException() : Exception("Concurrency conflict.");