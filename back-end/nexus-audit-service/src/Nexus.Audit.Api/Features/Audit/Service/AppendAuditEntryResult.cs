using Nexus.Audit.Api.Features.Audit.Models;

namespace Nexus.Audit.Api.Features.Audit.Service;

/// <summary>
/// Outcome of <see cref="IAuditService.AppendAsync"/>.
/// </summary>
/// <param name="Entry">The stored entry — either the freshly-inserted row or the one matched by the idempotency lookup.</param>
/// <param name="IsReplay">
/// <c>true</c> if the request matched an existing <c>(SourceService, IdempotencyKey)</c> pair with an identical payload
/// (the controller surfaces this as <c>X-Idempotent-Replay: true</c> and HTTP 200); <c>false</c> if a new entry was inserted (HTTP 201).
/// </param>
public sealed record AppendAuditEntryResult(AuditEntry Entry, bool IsReplay);
