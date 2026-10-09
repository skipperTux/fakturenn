using Fakturenn.Modules.Projects.Contracts;
using Fakturenn.Modules.Projects.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.Modules.Projects.Features;

public sealed class ListProjects(ProjectsDbContext db)
{
    // public Methods
    public async Task<IReadOnlyList<ProjectSnapshot>> HandleAsync(Guid customerId, CancellationToken cancellationToken) =>
        await db.Projects.AsNoTracking()
            .Where(p => p.CustomerId == customerId)
            .OrderBy(p => p.Reference)
            .Select(p => new ProjectSnapshot(p.Id, p.CustomerId, p.Reference))
            .ToListAsync(cancellationToken);
}
