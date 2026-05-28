using Nexus.Doctors.Api.Features.Doctors.Repository;

namespace Nexus.Doctors.Api.Features.Doctors.Service;

/// <summary>Mints public doctor codes in the form <c>DOC-YYYY-BRN-NNNNNN</c>.</summary>
public interface IPublicCodeGenerator
{
    Task<string> NextAsync(string branchCode, short year, CancellationToken ct);
}

/// <inheritdoc />
public sealed class PublicCodeGenerator : IPublicCodeGenerator
{
    private readonly IDoctorCodeSequenceRepository _sequences;

    public PublicCodeGenerator(IDoctorCodeSequenceRepository sequences) => _sequences = sequences;

    public async Task<string> NextAsync(string branchCode, short year, CancellationToken ct)
    {
        var code = branchCode.Trim().ToUpperInvariant();
        var n = await _sequences.NextAsync(code, year, ct);
        return $"DOC-{year:D4}-{code}-{n:D6}";
    }
}
