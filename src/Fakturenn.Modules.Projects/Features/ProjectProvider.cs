using Fakturenn.Modules.Projects.Contracts;
using Fakturenn.Modules.Projects.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.Modules.Projects.Features;

public sealed class ProjectProvider(ProjectsDbContext db) : IProjectProvider
{
    // public Methods
    public async Task<ProjectSnapshot?> FindAsync(Guid projectId, CancellationToken cancellationToken) =>
        await db.Projects.AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => new ProjectSnapshot(p.Id, p.CustomerId, p.Reference))
            .SingleOrDefaultAsync(cancellationToken);
}
