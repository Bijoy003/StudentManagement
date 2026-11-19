using System.Security.Claims;
using StudentManagement.Web.Models;

namespace StudentManagement.Web.Services;

public interface IChatIndexModelFactory
{
    Task<ChatIndexViewModel> BuildAsync(ClaimsPrincipal user);
}
