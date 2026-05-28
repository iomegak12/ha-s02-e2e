using Microsoft.EntityFrameworkCore;
using Nexus.Doctors.Api.Features.Doctors.Models;
using Nexus.Doctors.Api.Infrastructure.Persistence;

namespace Nexus.Doctors.Api.Features.Doctors.Documents.Repository;

/// <summary>Data access for <see cref="DoctorDocument"/>.</summary>
public interface IDoctorDocumentRepository
{
    Task<List<DoctorDocument>> ListAsync(Guid doctorId, bool includeDeleted, CancellationToken ct);
    Task<DoctorDocument?> GetAsync(Guid doctorId, Guid documentId, CancellationToken ct);
    Task<DoctorDocument?> FindActiveBySha256Async(Guid doctorId, string sha256, CancellationToken ct);
    Task AddAsync(DoctorDocument doc, CancellationToken ct);
    Task<int> SaveChangesAsync(CancellationToken ct);
}

/// <inheritdoc />
public sealed class DoctorDocumentRepository : IDoctorDocumentRepository
{
    private readonly DoctorDbContext _db;

    public DoctorDocumentRepository(DoctorDbContext db) => _db = db;

    public Task<List<DoctorDocument>> ListAsync(Guid doctorId, bool includeDeleted, CancellationToken ct)
    {
        var q = _db.DoctorDocuments.AsNoTracking().Where(d => d.DoctorId == doctorId);
        if (!includeDeleted) q = q.Where(d => d.DeletedAtUtc == null);
        return q.OrderByDescending(d => d.UploadedAtUtc).ToListAsync(ct);
    }

    public Task<DoctorDocument?> GetAsync(Guid doctorId, Guid documentId, CancellationToken ct) =>
        _db.DoctorDocuments.FirstOrDefaultAsync(d => d.DoctorId == doctorId && d.Id == documentId, ct);

    public Task<DoctorDocument?> FindActiveBySha256Async(Guid doctorId, string sha256, CancellationToken ct) =>
        _db.DoctorDocuments.AsNoTracking()
            .FirstOrDefaultAsync(d => d.DoctorId == doctorId && d.Sha256 == sha256 && d.DeletedAtUtc == null, ct);

    public async Task AddAsync(DoctorDocument doc, CancellationToken ct)
    {
        _db.DoctorDocuments.Add(doc);
        await _db.SaveChangesAsync(ct);
    }

    public Task<int> SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}
