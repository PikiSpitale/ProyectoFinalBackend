using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using ProyectAPI.Services;

namespace ProyectAPI.Controllers;

public sealed class TeamExceptionFilter : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        if (context.Exception is TeamOperationException error)
        {
            context.Result = new ObjectResult(new { message = error.Message }) { StatusCode = error.StatusCode };
            context.ExceptionHandled = true;
        }
    }
}
