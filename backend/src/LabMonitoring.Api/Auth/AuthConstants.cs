namespace LabMonitoring.Api.Auth;

public static class AuthSchemes
{
    /// <summary>Keycloak JWT bearer — the default scheme for admin endpoints.</summary>
    public const string Jwt = "Bearer";

    /// <summary>Opaque per-machine token: <c>Authorization: AgentToken &lt;token&gt;</c>.</summary>
    public const string AgentToken = "AgentToken";
}

public static class AuthPolicies
{
    public const string Administrator = "role:Administrator";
    public const string Technician = "role:Technician";
    public const string Teacher = "role:Teacher";

    /// <summary>Any admin-console role (Administrator, Technician or Teacher).</summary>
    public const string AdminConsole = "admin-console";

    /// <summary>Administrator or Teacher — session lifecycle actions (SRS FR-6.2/6.3).</summary>
    public const string TeacherOrAdmin = "teacher-or-admin";

    /// <summary>An approved Agent (registration accepted by a technician).</summary>
    public const string ApprovedAgent = "approved-agent";
}

public static class LabClaimTypes
{
    /// <summary>Local <c>Users.Id</c>, added by the provisioning middleware.</summary>
    public const string LocalUserId = "lab:uid";

    /// <summary>App role from the local database, added by the provisioning middleware.</summary>
    public const string Role = "lab:role";

    // Agent claims
    public const string ComputerId = "lab:computer_id";
    public const string MachineId = "lab:machine_id";
    public const string RoomId = "lab:room_id";
    public const string AgentApproved = "lab:agent_approved";
}
