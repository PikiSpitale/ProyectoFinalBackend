using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using ProyectAPI.Models;
using ProyectAPI.Services;
using System.Security.Claims;
namespace ProyectAPI.Controllers;

[ApiController, Authorize, TournamentEconomyExceptionFilter, Route("api/tournaments/{id}")]
public sealed class TournamentEconomyController(TournamentEconomyService service) : ControllerBase
{
    private string Actor => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new WalletOperationException(401,"invalid_session","Sesión no válida.");
    [HttpGet("enrollments")]
    public async Task<IActionResult> Requests(string id,CancellationToken ct) => Ok(await service.Requests(id,Actor,ct));
    [HttpPost("enrollments/{requestId}/confirm")]
    public async Task<IActionResult> Confirm(string id,string requestId,CancellationToken ct) => Ok(await service.Confirm(id,requestId,Actor,ct));
    [HttpPost("enrollments/{requestId}/cancel")]
    public async Task<IActionResult> CancelRequest(string id,string requestId,CancellationToken ct) => Ok(new { cancelled=await service.CancelRequest(id,requestId,Actor,ct) });
    [HttpPost("cancel"), Authorize(Roles=AppRoles.Staff)]
    public async Task<IActionResult> Cancel(string id,CancellationToken ct) => await service.Cancel(id,ct)?Ok(new{message="Torneo cancelado; puntos devueltos."}):NotFound();
    [HttpPost("close"), Authorize(Roles=AppRoles.Staff)]
    public async Task<IActionResult> Close(string id,CancellationToken ct) => await service.Close(id,Actor,ct)?Ok(new{message="Cierre definitivo y premios liquidados."}):NotFound();
    [HttpPost("adapt"), Authorize(Roles=AppRoles.Staff)]
    public async Task<IActionResult> Adapt(string id,CancellationToken ct) => await service.Run(id,(t,w,token)=>Task.FromResult(t!=null),ct,true)?Ok(new{message="Economía actualizada."}):NotFound();
}
public sealed class TournamentEconomyExceptionFilter : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        if(context.Exception is WalletOperationException w) context.Result=new ObjectResult(new{code=w.Code,message=w.Message}){StatusCode=w.StatusCode};
        else if(context.Exception is InvalidOperationException e) context.Result=new BadRequestObjectResult(new{message=e.Message});
        else return;
        context.ExceptionHandled=true;
    }
}
