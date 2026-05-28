using Microsoft.EntityFrameworkCore;
using Nexus.Doctors.Api.Features.Doctors.Models;
using Nexus.Doctors.Api.Infrastructure.Persistence;

namespace Nexus.Doctors.Api.Features.Doctors.Repository;

/// <summary>EF Core implementation of <see cref="IDoctorCodeSequenceRepository"/>.</summary>
public sealed class DoctorCodeSequenceRepository : IDoctorCodeSequenceRepository
{
    private readonly DoctorDbContext _db;

    public DoctorCodeSequenceRepository(DoctorDbContext db) => _db = db;

    public async Task<int> NextAsync(string branchCode, short year, CancellationToken ct)
    {
        var key = branchCode.Trim().ToUpperInvariant();
        var row = await _db.DoctorCodeSequences
            .FirstOrDefaultAsync(s => s.BranchCode == key && s.Year == year, ct);

        if (row is null)
        {
            row = new DoctorCodeSequence { BranchCode = key, Year = year, LastValue = 0 };
            _db.DoctorCodeSequences.Add(row);
        }

        row.LastValue++;
        await _db.SaveChangesAsync(ct);
        return row.LastValue;
    }
}
