using Nexus.Patients.Api.Features.Patients.Repository;

namespace Nexus.Patients.Api.Features.Patients.Service;

/// <summary>Mints public patient codes in the form <c>PAT-YYYY-BRN-NNNNNN</c>.</summary>
public interface IPublicCodeGenerator
{
    /// <summary>Reserve the next code for <paramref name="branchCode"/> in the given UTC year.</summary>
    Task<string> NextAsync(string branchCode, short year, CancellationToken ct);
}

/// <inheritdoc />
public sealed class PublicCodeGenerator : IPublicCodeGenerator
{
    private readonly IPatientCodeSequenceRepository _sequences;

    public PublicCodeGenerator(IPatientCodeSequenceRepository sequences) => _sequences = sequences;

    /// <inheritdoc />
    public async Task<string> NextAsync(string branchCode, short year, CancellationToken ct)
    {
        var code = branchCode.Trim().ToUpperInvariant();
        var n = await _sequences.NextAsync(code, year, ct);
        return $"PAT-{year:D4}-{code}-{n:D6}";
    }
}
