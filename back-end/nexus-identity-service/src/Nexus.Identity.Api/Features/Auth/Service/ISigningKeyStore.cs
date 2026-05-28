using Nexus.Identity.Api.Infrastructure.Persistence.Entities;

namespace Nexus.Identity.Api.Features.Auth.Service;

/// <summary>
/// Storage and lifecycle for RSA signing keys. Bootstraps a key on first read
/// if none exists. Rotation is out of scope for Phase 3.
/// </summary>
public interface ISigningKeyStore
{
    /// <summary>Return the currently active (non-retired) signing key, creating one if necessary.</summary>
    Task<SigningKey> GetActiveAsync(CancellationToken cancellationToken);

    /// <summary>Return every non-retired key, in newest-first order. Used to publish JWKS.</summary>
    Task<IReadOnlyList<SigningKey>> GetPublishedAsync(CancellationToken cancellationToken);
}
