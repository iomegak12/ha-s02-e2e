using Microsoft.EntityFrameworkCore;
using Nexus.Patients.Api.Features.Patients.Models;
using Nexus.Patients.Api.Infrastructure.Pagination;
using Nexus.Patients.Api.Infrastructure.Persistence;

namespace Nexus.Patients.Api.Features.Patients.Repository;

/// <summary>EF Core implementation of <see cref="IPatientRepository"/>.</summary>
public sealed class PatientRepository : IPatientRepository
{
    private readonly PatientDbContext _db;

    /// <summary>Create a new <see cref="PatientRepository"/>.</summary>
    public PatientRepository(PatientDbContext db) => _db = db;

    public async Task<Patient> AddAsync(Patient entity, CancellationToken ct)
    {
        _db.Patients.Add(entity);
        await _db.SaveChangesAsync(ct);
        return entity;
    }

    public Task<Patient?> GetByIdAsync(Guid id, CancellationToken ct) =>
        _db.Patients.Include(p => p.BranchLinks).FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<Patient?> GetByIdNoTrackingAsync(Guid id, CancellationToken ct) =>
        _db.Patients.AsNoTracking().Include(p => p.BranchLinks).FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<Patient?> FindByPhoneDobAsync(string phone, DateOnly dateOfBirth, CancellationToken ct) =>
        _db.Patients.AsNoTracking().FirstOrDefaultAsync(p => p.Phone == phone && p.DateOfBirth == dateOfBirth, ct);

    public Task<Patient?> FindByEmailDobAsync(string email, DateOnly dateOfBirth, CancellationToken ct) =>
        _db.Patients.AsNoTracking().FirstOrDefaultAsync(p => p.Email == email && p.DateOfBirth == dateOfBirth, ct);

    public Task<List<PatientBranchLink>> GetActiveLinksAsync(Guid patientId, CancellationToken ct) =>
        _db.PatientBranchLinks.AsNoTracking()
            .Where(l => l.PatientId == patientId && l.UnlinkedAtUtc == null)
            .ToListAsync(ct);

    public Task<int> SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);

    public async Task<PagedResult<Patient>> QueryAsync(PatientQuery query, CancellationToken ct)
    {
        var q = _db.Patients.AsNoTracking().AsQueryable();

        if (query.Status is { } status) q = q.Where(p => p.Status == status);
        if (query.BranchId is { } branchId)
        {
            q = q.Where(p => p.BranchLinks.Any(l => l.BranchId == branchId && l.UnlinkedAtUtc == null));
        }
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var s = query.Search.Trim();
            q = q.Where(p => p.FullName.Contains(s) || p.PublicCode.Contains(s)
                || (p.Phone != null && p.Phone.Contains(s))
                || (p.Email != null && p.Email.Contains(s)));
        }

        var page = new PageRequest(query.Page, query.Size);
        var total = await q.LongCountAsync(ct);
        var items = await q.OrderByDescending(p => p.CreatedAtUtc).Skip(page.Skip).Take(page.NormalizedSize).ToListAsync(ct);
        return new PagedResult<Patient>(items, page.NormalizedPage, page.NormalizedSize, total);
    }
}
