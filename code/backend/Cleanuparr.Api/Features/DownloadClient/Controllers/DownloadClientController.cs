using System;
using System.Linq;

using Cleanuparr.Api.Extensions;
using Cleanuparr.Api.Features.DownloadClient.Contracts.Requests;
using Cleanuparr.Api.Features.DownloadClient.Contracts.Responses;
using Cleanuparr.Domain.Enums;
using Cleanuparr.Infrastructure.Features.DownloadClient;
using Cleanuparr.Infrastructure.Features.DownloadClient.Usenet;
using Cleanuparr.Infrastructure.Http.DynamicHttpClientSystem;
using Cleanuparr.Persistence;
using Cleanuparr.Persistence.Models.Configuration;
using Cleanuparr.Shared.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cleanuparr.Api.Features.DownloadClient.Controllers;

[ApiController]
[Route("api/configuration")]
[Authorize]
public sealed class DownloadClientController : ControllerBase
{
    private readonly EventsContext? _events;
    private readonly ILogger<DownloadClientController> _logger;
    private readonly DataContext _dataContext;
    private readonly IDynamicHttpClientFactory _dynamicHttpClientFactory;
    private readonly IDownloadServiceFactory _downloadServiceFactory;

    public DownloadClientController(
        ILogger<DownloadClientController> logger,
        DataContext dataContext,
        IDynamicHttpClientFactory dynamicHttpClientFactory,
        IDownloadServiceFactory downloadServiceFactory,
        EventsContext? events = null)
    {
        _events = events;
        _logger = logger;
        _dataContext = dataContext;
        _dynamicHttpClientFactory = dynamicHttpClientFactory;
        _downloadServiceFactory = downloadServiceFactory;
    }

    [HttpGet("download_client")]
    public async Task<IActionResult> GetDownloadClientConfig()
    {
        await DataContext.Lock.WaitAsync();
        try
        {
            List<DownloadClientConfig> clientConfigs = await _dataContext.DownloadClients
                .AsNoTracking()
                .ToListAsync();

            List<DownloadClientConfigResponse> clients = clientConfigs
                .Where(c => !EnumSentinel.IsUnknown(c.TypeName) && !EnumSentinel.IsUnknown(c.Type))
                .OrderBy(c => c.TypeName)
                .ThenBy(c => c.Name)
                .Select(DownloadClientConfigResponse.From)
                .ToList();

            return Ok(new { clients });
        }
        finally
        {
            DataContext.Lock.Release();
        }
    }

    [HttpPost("download_client")]
    public async Task<IActionResult> CreateDownloadClientConfig([FromBody] CreateDownloadClientRequest newClient)
    {
        await DataContext.Lock.WaitAsync();
        try
        {
            newClient.Validate();

            var clientConfig = newClient.ToEntity();
            clientConfig.Validate();

            _dataContext.DownloadClients.Add(clientConfig);
            await _dataContext.SaveChangesAsync();

            return CreatedAtAction(nameof(GetDownloadClientConfig), new { id = clientConfig.Id }, DownloadClientConfigResponse.From(clientConfig));
        }
        finally
        {
            DataContext.Lock.Release();
        }
    }

    [HttpPut("download_client/{id}")]
    public async Task<IActionResult> UpdateDownloadClientConfig(Guid id, [FromBody] UpdateDownloadClientRequest updatedClient)
    {
        await DataContext.Lock.WaitAsync();
        try
        {
            updatedClient.Validate();

            var existingClient = await _dataContext.DownloadClients
                .FirstOrDefaultAsync(c => c.Id == id);

            if (existingClient is null)
            {
                return this.ProblemResult(StatusCodes.Status404NotFound, $"Download client with ID {id} not found");
            }

            var clientToPersist = updatedClient.ApplyTo(existingClient);
            clientToPersist.Validate();

            _dataContext.Entry(existingClient).CurrentValues.SetValues(clientToPersist);
            await _dataContext.SaveChangesAsync();

            return Ok(DownloadClientConfigResponse.From(clientToPersist));
        }
        finally
        {
            DataContext.Lock.Release();
        }
    }

    [HttpDelete("download_client/{id}")]
    public async Task<IActionResult> DeleteDownloadClientConfig(Guid id)
    {
        await DataContext.Lock.WaitAsync();
        try
        {
            var existingClient = await _dataContext.DownloadClients
                .FirstOrDefaultAsync(c => c.Id == id);

            if (existingClient is null)
            {
                return this.ProblemResult(StatusCodes.Status404NotFound, $"Download client with ID {id} not found");
            }

            _dataContext.DownloadClients.Remove(existingClient);
            await _dataContext.SaveChangesAsync();

            var clientName = $"DownloadClient_{id}";
            _dynamicHttpClientFactory.UnregisterConfiguration(clientName);

            _logger.LogInformation("Removed HTTP client configuration for deleted download client {ClientName}", clientName);

            return NoContent();
        }
        finally
        {
            DataContext.Lock.Release();
        }
    }

    [HttpGet("download_client/{id}/usenet-status")]
    public async Task<IActionResult> GetUsenetStatus(Guid id, CancellationToken cancellationToken)
    {
        if (!await _dataContext.DownloadClients.AnyAsync(x => x.Id == id && x.TypeName == DownloadClientTypeName.NZBGet, cancellationToken))
            return NotFound();
        if (_events is null) return StatusCode(503);
        var rows = await _events.UsenetObservations.AsNoTracking().Where(x => x.ClientId == id)
            .OrderByDescending(x => x.ActionState != "").ThenByDescending(x => x.LastSeenTicks).Take(200)
            .Select(x => new { x.Id, x.OwnerId, x.LastSeenTicks, x.Samples, x.Incident, x.ActionState, x.Fingerprint }).ToListAsync(cancellationToken);
        return Ok(rows.Select(x => new { x.Id, x.OwnerId, x.LastSeenTicks, x.Samples, x.Incident, x.ActionState,
            Title = x.Id.EndsWith(":client", StringComparison.Ordinal) ? "Client recovery" : x.Fingerprint.Split('|')[0] }));
    }

    public sealed record RecoveryReviewRequest(bool AcknowledgeReview);

    [HttpPost("download_client/{id}/usenet-review")]
    public async Task<IActionResult> ReviewUsenetRecovery(Guid id, [FromBody] RecoveryReviewRequest request, CancellationToken cancellationToken)
    {
        if (!request.AcknowledgeReview) return BadRequest("Inspect NZBGet and the owning arr before acknowledging review");
        if (_events is null) return StatusCode(503);
        await UsenetQueueCoordinator.RecoveryLock.WaitAsync(cancellationToken);
        try
        {
            var config = await _dataContext.DownloadClients.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (config?.TypeName != DownloadClientTypeName.NZBGet) return NotFound();
            var state = await _events.UsenetObservations.FindAsync([id + ":client"], cancellationToken);
            if (state?.ActionState != "Halted") return BadRequest("This client has no recovery hold to review");
            using var service = _downloadServiceFactory.GetDownloadService(config);
            if (service is not IUsenetDownloadService usenet) return BadRequest("Usenet inspection is unavailable");
            UsenetSnapshot snapshot;
            try { snapshot = await usenet.InspectAsync(cancellationToken); }
            catch (Exception) { return BadRequest("Queue inspection failed; the recovery hold remains active"); }
            if (!snapshot.Safe || !snapshot.TotalDownloadedBytes.HasValue) return BadRequest("Resolve client safety constraints before releasing the recovery hold");
            state.ActionState = "AwaitingProgress";
            state.ActionTicks = DateTimeOffset.UtcNow.UtcTicks;
            state.LastSeenTicks = state.ActionTicks;
            state.RecoveryProgress = snapshot.TotalDownloadedBytes.Value.ToString();
            state.Incident = "Manual review acknowledged; waiting for renewed progress. Previous attempts remain protected.";
            await _events.SaveChangesAsync(cancellationToken);
            return Ok(new { Message = state.Incident });
        }
        finally { UsenetQueueCoordinator.RecoveryLock.Release(); }
    }

    [HttpPost("download_client/test")]
    public async Task<IActionResult> TestDownloadClient([FromBody] TestDownloadClientRequest request)
    {
        try
        {
            request.Validate();

            string? resolvedPassword = null;

            if (request.Password.IsPlaceholder() && request.ClientId.HasValue)
            {
                var existingClient = await _dataContext.DownloadClients
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == request.ClientId.Value);

                if (existingClient is null)
                {
                    return this.ProblemResult(StatusCodes.Status404NotFound, $"Download client with ID {request.ClientId.Value} not found");
                }

                resolvedPassword = existingClient.Password;
            }

            var testConfig = request.ToTestConfig(resolvedPassword);
            testConfig.Validate();
            using var downloadService = _downloadServiceFactory.GetDownloadService(testConfig);
            var healthResult = await downloadService.HealthCheckAsync();

            if (healthResult.IsHealthy)
            {
                return Ok(new
                {
                    Message = $"Connection to {request.TypeName} successful",
                    ResponseTime = healthResult.ResponseTime.TotalMilliseconds
                });
            }

            return this.ProblemResult(StatusCodes.Status400BadRequest, healthResult.ErrorMessage ?? "Connection failed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to test {TypeName} client connection", request.TypeName);
            return this.ProblemResult(StatusCodes.Status400BadRequest, $"Connection failed: {ex.Message}");
        }
    }
}
