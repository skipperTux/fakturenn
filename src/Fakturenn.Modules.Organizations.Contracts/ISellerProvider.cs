namespace Fakturenn.Modules.Organizations.Contracts;

public interface ISellerProvider
{
    Task<SellerSnapshot> GetAsync(CancellationToken cancellationToken);
}
