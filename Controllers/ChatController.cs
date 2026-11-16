using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProyectoFinalBackend.Models;
using ProyectoFinalBackend.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;

namespace ProyectoFinalBackend.Controllers
{
    [ApiController]
    [Route("api/chat")]
    [Authorize]
    public class ChatController : ControllerBase
    {
        private readonly ChatService _chatSvc;

        public ChatController(ChatService chatSvc) => _chatSvc = chatSvc;

        private string GetUserId() => HttpContext.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";

        [HttpGet("verificar-permiso")]
        public IActionResult VerificarPermiso([FromQuery] string receiverId)
        {
            var userId = GetUserId();
            var (ok, motivo, deliveryStatus) = _chatSvc.VerificarPermiso(userId, receiverId);
            return Ok(new { ok, motivo, deliveryStatus });
        }

        [HttpPost("enviar")]
        public IActionResult EnviarMensaje([FromBody] EnviarMensajeDto dto)
        {
            var userId = GetUserId();
            try
            {
                _chatSvc.EnviarMensaje(userId, dto.ReceiverId, dto.Contenido);
                return Ok();
            }
            catch { return BadRequest(); }
        }

        [HttpPost("aceptar-solicitud/{mensajeId}")]
        public IActionResult AceptarSolicitud(string mensajeId)
        {
            var userId = GetUserId();
            try
            {
                _chatSvc.AceptarSolicitud(mensajeId, userId);
                return Ok();
            }
            catch { return Forbid(); }
        }

        [HttpPut("editar/{mensajeId}")]
        public IActionResult EditarMensaje(string mensajeId, [FromBody] EditarMensajeDto dto)
        {
            var result = _chatSvc.EditarMensaje(mensajeId, GetUserId(), dto.NuevoContenido);
            if (result.success) return Ok();
            if (result.error.Contains("permisos")) return Forbid();
            return NotFound();
        }

        [HttpDelete("eliminar/{mensajeId}")]
        public IActionResult EliminarMensaje(string mensajeId)
        {
            var result = _chatSvc.EliminarMensaje(mensajeId, GetUserId());
            if (result.success) return Ok();
            if (result.error.Contains("permisos")) return Forbid();
            return NotFound();
        }

        [HttpPost("denunciar")]
        public IActionResult Denunciar([FromBody] ReportarDto dto)
        {
            try
            {
                var userId = GetUserId();
                _chatSvc.Denunciar(userId, dto.TargetId, dto.MessageId, dto.Reason);
                return Ok();
            }
            catch { return BadRequest(); }
        }

        [HttpPost("resolver-denuncia")]
        public IActionResult ResolverDenuncia(string reporteId, [FromBody] ResolverDenunciaDto dto)
        {
            var ok = _chatSvc.ResolverDenuncia(reporteId, GetUserId(), dto.Accion);
            return Ok(new { ok });
        }

        [HttpPost("seguir")]
        public IActionResult Seguir(string targetId)
        {
            var ok = _chatSvc.Seguir(GetUserId(), targetId);
            return Ok(ok);
        }

        [HttpPost("dejar-seguir")]
        public IActionResult DejarSeguir(string targetId)
        {
            var ok = _chatSvc.DejarDeSeguir(GetUserId(), targetId);
            return Ok(ok);
        }

        [HttpGet("usuarios-sigo")]
        public IActionResult UsuariosQueSigo()
        {
            return Ok(_chatSvc.UsuariosQueSigo(GetUserId()));
        }

        [HttpGet("mensajes-directos")]
        public IActionResult MensajesDirectos()
        {
            return Ok(_chatSvc.MensajesDirectos(GetUserId()));
        }

        [HttpGet("solicitudes-pendientes")]
        public IActionResult SolicitudesPendientes()
        {
            return Ok(_chatSvc.SolicitudesPendientes(GetUserId()));
        }

        [HttpGet("contar-solicitudes")]
        public IActionResult ContarSolicitudes()
        {
            return Ok(_chatSvc.ContarSolicitudes(GetUserId()));
        }

        [HttpGet("perfil/{id}")]
        public IActionResult Perfil(string id)
        {
            var currentUserId = GetUserId();
            var yaSigue = _chatSvc.YaSigue(currentUserId, id);

            return Ok(new PerfilViewModel
            {
                CurrentUserId = currentUserId,
                ProfileUserId = id,
                YaSigue = yaSigue
            });
        }
    }

    public class PerfilViewModel
    {
        public string CurrentUserId { get; set; }
        public string ProfileUserId { get; set; }
        public bool YaSigue { get; set; }
    }
}