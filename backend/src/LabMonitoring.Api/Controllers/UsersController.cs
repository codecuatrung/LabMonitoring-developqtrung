using LabMonitoring.Api.Auth;
using LabMonitoring.Application.Common.Models;
using LabMonitoring.Application.Users;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LabMonitoring.Api.Controllers;

/// <summary>User/role/room-scope/screen-override management (SRS FR-8.1/8.2). Accounts themselves
/// are provisioned by Keycloak sign-in (JIT), not created here — see the module doc comment in
/// UserManagementHandlers.</summary>
[ApiController]
[Route("api/v1/users")]
[Authorize(Policy = "screen:" + Domain.Common.SeededScreens.Users)]
public sealed class UsersController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<UserSummaryDto>>> List(
        [FromQuery] string? role, [FromQuery] bool includeInactive,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => Ok(await sender.Send(new GetUsersQuery(role, includeInactive, page, pageSize), ct));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<UserDetailDto>> Get(int id, CancellationToken ct)
        => Ok(await sender.Send(new GetUserQuery(id), ct));

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateUserInput input, CancellationToken ct)
    {
        await sender.Send(new UpdateUserCommand(id, input.FullName, input.Phone, input.IsActive), ct);
        return NoContent();
    }

    [HttpPut("{id:int}/roles")]
    public async Task<IActionResult> SetRoles(int id, [FromBody] IReadOnlyList<int> roleIds, CancellationToken ct)
    {
        await sender.Send(new SetUserRolesCommand(id, roleIds), ct);
        return NoContent();
    }

    [HttpPut("{id:int}/room-scopes")]
    public async Task<IActionResult> SetRoomScopes(int id, [FromBody] IReadOnlyList<int> roomIds, CancellationToken ct)
    {
        await sender.Send(new SetUserRoomScopesCommand(id, roomIds), ct);
        return NoContent();
    }

    [HttpPut("{id:int}/screen-overrides")]
    public async Task<IActionResult> SetScreenOverrides(
        int id, [FromBody] IReadOnlyList<UserScreenOverrideDto> overrides, CancellationToken ct)
    {
        await sender.Send(new SetUserScreenOverridesCommand(id, overrides), ct);
        return NoContent();
    }

    public sealed record UpdateUserInput(string FullName, string? Phone, bool IsActive);
}
