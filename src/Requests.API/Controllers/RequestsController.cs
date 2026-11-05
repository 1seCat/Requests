using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Requests.Application.DTOs;
using Requests.Application.Services;
using Requests.Domain.Enums;
using Requests.Domain.Exceptions;

namespace Requests.API.Controllers;

[ApiController]
[Route("api/requests")]
public sealed class RequestsController : ControllerBase
{
    private readonly RequestService _svc;
    private readonly IValidator<CreateRequestRequest> _createV;
    private readonly IValidator<UpdateStatusRequest> _statusV;

    public RequestsController(
        RequestService svc,
        IValidator<CreateRequestRequest> createV,
        IValidator<UpdateStatusRequest> statusV)
    { _svc = svc; _createV = createV; _statusV = statusV; }

    [HttpPost]
    [ProducesResponseType(typeof(RequestDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateRequestRequest dto, CancellationToken ct)
    {
        var v = await _createV.ValidateAsync(dto, ct);
        if (!v.IsValid) return ValidationProblem(new ValidationProblemDetails(v.ToDictionary()));

        try
        {
            var created = await _svc.CreateAsync(dto, ct);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }
        catch (DuplicateExternalIdException ex)
        {
            return Conflict(new ProblemDetails { Title = "Duplicate ExternalId", Detail = ex.Message, Status = 409 });
        }
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<RequestDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List(
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string sortBy = "CreatedAt",
        [FromQuery] string sortDirection = "DESC",
        CancellationToken ct = default)
    {
        if (page < 1) return BadRequestProblem("page must be >= 1");
        if (pageSize < 1 || pageSize > 100) return BadRequestProblem("pageSize must be between 1 and 100");
        if (!string.Equals(sortBy, "CreatedAt", StringComparison.OrdinalIgnoreCase))
            return BadRequestProblem("sortBy is not supported");
        if (sortDirection is not ("ASC" or "DESC"))
            return BadRequestProblem("sortDirection must be ASC or DESC");

        RequestStatus? st = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<RequestStatus>(status, true, out var parsed))
                return BadRequestProblem("Unknown status");
            st = parsed;
        }

        var result = await _svc.ListAsync(new RequestFilter(st, page, pageSize, sortBy, sortDirection), ct);
        return Ok(result);
    }

    [HttpGet("{id:long}")]
    [ProducesResponseType(typeof(RequestDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(long id, CancellationToken ct)
    {
        var dto = await _svc.GetAsync(id, ct);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpPatch("{id:long}/status")]
    [ProducesResponseType(typeof(RequestDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateStatus(long id, [FromBody] UpdateStatusRequest dto, CancellationToken ct)
    {
        var v = await _statusV.ValidateAsync(dto, ct);
        if (!v.IsValid) return ValidationProblem(new ValidationProblemDetails(v.ToDictionary()));

        try
        {
            var updated = await _svc.UpdateStatusAsync(id, dto, ct);
            return Ok(updated);
        }
        catch (RequestNotFoundException) { return NotFound(); }
        catch (InvalidStatusTransitionException ex) { return Conflict(new ProblemDetails { Title = "Prohibited transition", Detail = ex.Message, Status = 409 }); }
        catch (ConcurrencyConflictException) { return Conflict(new ProblemDetails { Title = "Concurrency conflict", Status = 409 }); }
        catch (ArgumentException ex) { return BadRequestProblem(ex.Message); }
        catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
        { return Conflict(new ProblemDetails { Title = "Concurrency conflict", Status = 409 }); }
    }

    private ObjectResult BadRequestProblem(string detail)
        => BadRequest(new ProblemDetails { Title = "Validation error", Detail = detail, Status = 400 });
}