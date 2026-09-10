namespace GeoDataPro.Core.Security;

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string? encoded);
    bool NeedsUpgrade(string? encoded);
}
