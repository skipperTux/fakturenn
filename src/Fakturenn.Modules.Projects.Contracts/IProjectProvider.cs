namespace Fakturenn.Modules.Projects.Contracts;

public interface IProjectProvider
{
    Task<ProjectSnapshot?> FindAsync(Guid projectId, CancellationToken cancellationToken);
}
