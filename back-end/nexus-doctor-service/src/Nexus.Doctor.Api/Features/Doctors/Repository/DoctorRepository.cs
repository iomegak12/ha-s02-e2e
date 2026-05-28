using Microsoft.EntityFrameworkCore;
using Nexus.Doctors.Api.Features.Doctors.Models;
using Nexus.Doctors.Api.Infrastructure.Pagination;
using Nexus.Doctors.Api.Infrastructure.Persistence;

namespace Nexus.Doctors.Api.Features.Doctors.Repository;

/// <summary>EF Core implementation of <see cref="IDoctorRepository"/>.</summary>
public sealed class DoctorRepository : IDoctorRepository
{
    private readonly DoctorDbContext _db;

    public DoctorRepository(DoctorDbContext db) => _db = db;

    public async Task<Doctor> AddAsync(Doctor entity, CancellationToken ct)
    {
        _db.Doctors.Add(entity);
        await _db.SaveChangesAsync(ct);
        return entity;
    }

    public Task<Doctor?> GetByIdAsync(Guid id, CancellationToken ct) =>
        _db.Doctors.Include(d => d.Documents).Include(d => d.BranchLinks)
            .FirstOrDefaultAsync(d => d.Id == id, ct);

    public Task<Doctor?> GetByIdNoTrackingAsync(Guid id, CancellationToken ct) =>
        _db.Doctors.AsNoTracking().Include(d => d.Documents).Include(d => d.BranchLinks)
            .FirstOrDefaultAsync(d => d.Id == id, ct);

    public Task<Doctor?> FindActiveByLicenseAsync(string licenseNumber, CancellationToken ct) =>
        _db.Doctors.AsNoTracking()
            .FirstOrDefaultAsync(d => d.LicenseNumber == licenseNumber && d.Status != DoctorStatus.Deactivated, ct);

    public Task<List<DoctorBranchLink>> GetActiveLinksAsync(Guid doctorId, CancellationToken ct) =>
        _db.DoctorBranchLinks.AsNoTracking()
            .Where(l => l.DoctorId == doctorId && l.UnlinkedAtUtc == null)
            .ToListAsync(ct);

    public Task<int> SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);

    public async Task<PagedResult<Doctor>> QueryAsync(DoctorQuery query, CancellationToken ct)
    {
        var q = _db.Doctors.AsNoTracking().AsQueryable();

        if (query.Status is { } status) q = q.Where(d => d.Status == status);
        if (query.BranchId is { } branchId)
        {
            q = q.Where(d => d.BranchLinks.Any(l => l.BranchId == branchId && l.UnlinkedAtUtc == null));
        }
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var s = query.Search.Trim();
            q = q.Where(d => d.FullName.Contains(s) || d.PublicCode.Contains(s)
                || d.LicenseNumber.Contains(s)
                || (d.Phone != null && d.Phone.Contains(s))
                || (d.Email != null && d.Email.Contains(s)));
        }

        var page = new PageRequest(query.Page, query.Size);
        var total = await q.LongCountAsync(ct);
        var items = await q.OrderByDescending(d => d.CreatedAtUtc).Skip(page.Skip).Take(page.NormalizedSize).ToListAsync(ct);
        return new PagedResult<Doctor>(items, page.NormalizedPage, page.NormalizedSize, total);
    }
}
