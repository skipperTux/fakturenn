using Fakturenn.Modules.Projects.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.Modules.Projects.Features;

public sealed class SaveProject(ProjectsDbContext db)
{
    // public Methods
    public async Task<Guid> HandleAsync(Guid? projectId, Guid customerId, string reference, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);

        Project project = projectId is Guid id
            ? await db.Projects.SingleAsync(p => p.Id == id && p.CustomerId == customerId, cancellationToken)
            : db.Projects.Add(new Project { Id = Guid.CreateVersion7(), CustomerId = customerId }).Entity;

        project.Reference = reference.Trim();
        await db.SaveChangesAsync(cancellationToken);
        return project.Id;
    }
}
