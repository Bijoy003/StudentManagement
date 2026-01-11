using StudentMangement.Data;

namespace StudentMangement.Abstraction.Services
{
    public interface IAuthService
    {
        string GenerateJwtToken(ApplicationUser user);
    }
}