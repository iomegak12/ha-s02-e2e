using Nexus.Web.Client.Models.Auth;
using Nexus.Web.Client.Models.Common;

namespace Nexus.Web.Client.Services.Auth;

public interface IAuthService
{
    Task<ApiResponse<SessionInfo>> LoginAsync(LoginRequest request);
    Task<ApiResponse<SessionInfo>> RefreshAsync();
    Task<ApiResponse<bool>>        LogoutAsync();
}
