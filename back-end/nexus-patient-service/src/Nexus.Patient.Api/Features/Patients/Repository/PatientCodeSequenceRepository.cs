using Microsoft.EntityFrameworkCore;
using Nexus.Patients.Api.Features.Patients.Models;
using Nexus.Patients.Api.Infrastructure.Persistence;

namespace Nexus.Patients.Api.Features.Patients.Repository;

/// <summary>EF Core implementation of <see cref="IPatientCodeSequenceRepository"/>.</summary>
public sealed class PatientCodeSequenceRepository : IPatientCodeSequenceRepository
{
    private readonly PatientDbContext _db;

    /// <summary>Create a new <see cref="PatientCodeSequenceRepository"/>.</summary>
    public PatientCodeSequenceRepository(PatientDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<int> NextAsync(string branchCode, short year, CancellationToken ct)
    {
        var key = branchCode.Trim().ToUpperInvariant();
        var row = await _db.PatientCodeSequences
            .FirstOrDefaultAsync(s => s.BranchCode == key && s.Year == year, ct);

        if (row is null)
        {
            row = new PatientCodeSequence
            {
                BranchCode = key,
                Year = year,
                LastValue = 0,
            };
            _db.PatientCodeSequences.Add(row);
        }

        row.LastValue++;
        await _db.SaveChangesAsync(ct);
        return row.LastValue;
    }
}
