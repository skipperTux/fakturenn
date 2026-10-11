using Fakturenn.Modules.Projects.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.Modules.Projects.Features;

public sealed class SaveProject(ProjectsDbContext db)
{
    // public Methods
    /// <summary>
    /// Adds a project, or renames one when <paramref name="projectId"/> is given. Adding a
    /// reference the customer already has returns that project: typing it twice is an
    /// ordinary mistake, not a second project.
    /// </summary>
    public async Task<Guid> HandleAsync(Guid? projectId, Guid customerId, string reference, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);

        string trimmed = reference.Trim();
        if (projectId is null)
        {
            Guid? existing = await db.Projects.AsNoTracking()
                .Where(p => p.CustomerId == customerId && p.Reference == trimmed)
                .Select(p => (Guid?)p.Id)
                .SingleOrDefaultAsync(cancellationToken);
            if (existing is Guid id)
            {
                return id;
            }
        }

        Project project = projectId is Guid projectToRename
            ? await db.Projects.SingleAsync(p => p.Id == projectToRename && p.CustomerId == customerId, cancellationToken)
            : db.Projects.Add(new Project { Id = Guid.CreateVersion7(), CustomerId = customerId }).Entity;

        project.Reference = trimmed;
        await db.SaveChangesAsync(cancellationToken);
        return project.Id;
    }
}
