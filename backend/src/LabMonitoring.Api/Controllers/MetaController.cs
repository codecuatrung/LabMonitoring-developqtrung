using System.Reflection;
using Microsoft.AspNetCore.Mvc;

namespace LabMonitoring.Api.Controllers;

[ApiController]
[Route("api/v1/meta")]
public sealed class MetaController : ControllerBase
{
    /// <summary>Unauthenticated liveness/info probe used for smoke tests and deployment checks.</summary>
    [HttpGet]
    public IActionResult Get() => Ok(new
    {
        service = "LabMonitoring.Api",
        version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(),
        environmentName = HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().EnvironmentName,
        utcNow = DateTime.UtcNow,
    });
}
