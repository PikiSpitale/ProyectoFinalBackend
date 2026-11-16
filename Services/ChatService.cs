using MongoDB.Bson;
using MongoDB.Driver;
using ProyectAPI.Infrastructure;
using ProyectoFinalBackend.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ProyectoFinalBackend.Services
{
    public class ChatService
    {
        private readonly IMongoDatabase _db;

        public ChatService(MongoDbContext context) => _db = context.Database;

        public (bool ok, string motivo, string deliveryStatus) VerificarPermiso(string userIdClaim, string receiverId)
        {
            if (!ObjectId.TryParse(userIdClaim, out var senderObjId))
                return (false, "ID de emisor inválido", "");
            if (!ObjectId.TryParse(receiverId, out var receiverObjId))
                return (false, "ID de receptor inválido", "");

            if (senderObjId == receiverObjId)
                return (false, "No puede enviarse mensaje al mismo usuario", "");

            var senderUser = _db.GetCollection<ChatUser>("users")
                .Find(Builders<ChatUser>.Filter.Eq(u => u.Id, senderObjId)).FirstOrDefault();
            var receiverUser = _db.GetCollection<ChatUser>("users")
                .Find(Builders<ChatUser>.Filter.Eq(u => u.Id, receiverObjId)).FirstOrDefault();

            if (senderUser == null || receiverUser == null)
                return (false, "Usuario no encontrado", "");

            if (senderUser.IsActive == false || receiverUser.IsActive == false)
                return (false, "Usuario baneado. No puede enviar mensajes", "");

            bool writerFollowsReceiver = senderUser.Following?.Any(f => f == receiverObjId) ?? false;
            bool receiverFollowsWriter = receiverUser.Following?.Any(f => f == senderObjId) ?? false;

            if (writerFollowsReceiver && receiverFollowsWriter)
                return (true, "", "direct");
            if (writerFollowsReceiver && !receiverFollowsWriter)
                return (true, "", "request");
            return (false, "Debe seguir al destinatario primero", "blocked");
        }

        public Message EnviarMensaje(string userIdClaim, string receiverId, string contenido)
        {
            if (!ObjectId.TryParse(userIdClaim, out var senderObjId) ||
                !ObjectId.TryParse(receiverId, out var receiverObjId))
                throw new ArgumentException("IDs inválidos");

            var (ok, _, deliveryStatus) = VerificarPermiso(userIdClaim, receiverId);
            if (!ok) throw new Exception("No tiene permiso");

            var msg = new Message
            {
                SenderId = senderObjId,
                ReceiverId = receiverObjId,
                Content = contenido,
                CreatedAt = DateTime.UtcNow,
                DeliveryStatus = deliveryStatus,
                Read = false
            };

            _db.GetCollection<Message>("messages").InsertOne(msg);
            return msg;
        }

        public (bool success, string error, Message mensaje) AceptarSolicitud(string mensajeId, string userIdClaim)
        {
            if (!ObjectId.TryParse(mensajeId, out var msgId))
                return (false, "ID de mensaje inválido", null);

            if (!ObjectId.TryParse(userIdClaim, out var senderUserId))
                return (false, "ID de usuario inválido", null);

            var usuario = _db.GetCollection<ChatUser>("users")
                .Find(Builders<ChatUser>.Filter.Eq(u => u.Id, senderUserId)).FirstOrDefault();

            if (usuario == null || !usuario.IsActive) return (false, "Usuario baneado. No puede aceptar solicitudes", null);

            var msg = _db.GetCollection<Message>("messages")
                .Find(Builders<Message>.Filter.Eq(m => m.Id, msgId)).FirstOrDefault();

            if (msg == null) return (false, "Mensaje no encontrado", null);

            if (msg.DeliveryStatus != "request") return (false, "No es una solicitud pendiente", null);

            if (msg.ReceiverId != senderUserId) return (false, "No tiene permiso para aceptar esta solicitud", null);

            _db.GetCollection<Message>("messages")
                .UpdateOne(Builders<Message>.Filter.And(
                    Builders<Message>.Filter.Eq(m => m.Id, msgId),
                    Builders<Message>.Filter.Eq(m => m.DeliveryStatus, "request")),
                    Builders<Message>.Update.Set(m => m.DeliveryStatus, "direct"));

            return (true, "", msg);
        }

        public (bool success, string error) EditarMensaje(string mensajeId, string userIdClaim, string nuevoContenido)
        {
            if (!ObjectId.TryParse(mensajeId, out var msgId))
                return (false, "ID inválido");

            if (!ObjectId.TryParse(userIdClaim, out var userId))
                return (false, "ID de usuario inválido");

            var msg = _db.GetCollection<Message>("messages")
                .Find(Builders<Message>.Filter.Eq(m => m.Id, msgId)).FirstOrDefault();

            if (msg == null) return (false, "Mensaje no encontrado");

            if (msg.Deleted) return (false, "El mensaje ya ha sido eliminado");

            var userActual = _db.GetCollection<ChatUser>("users")
                .Find(Builders<ChatUser>.Filter.Eq(u => u.Id, userId)).FirstOrDefault();

            var esModerador = userActual?.Role == "Moderator";

            var tiempoLimite = TimeSpan.FromMinutes(2);
            var ahora = DateTime.UtcNow;
            var transcurso = ahora - msg.CreatedAt;

            if (userActual == null || !userActual.IsActive) return (false, "Usuario baneado. No puede eliminar mensajes");

            if (!esModerador && transcurso > tiempoLimite)
                return (false, "Límite de 2 minutos superado");

            bool esRemitente = msg.SenderId == userId;

            if (!esRemitente && !esModerador)
                return (false, "Sin permisos");

            _db.GetCollection<Message>("messages")
                .UpdateOne(Builders<Message>.Filter.Eq(m => m.Id, msgId),
                    Builders<Message>.Update
                        .Set(m => m.Content, nuevoContenido)
                        .Set(m => m.Edited, true)
                        .Set(m => m.EditedAt, DateTime.UtcNow));

            return (true, "");
        }

        public (bool success, string error) EliminarMensaje(string mensajeId, string userIdClaim)
        {
            if (!ObjectId.TryParse(mensajeId, out var msgId))
                return (false, "ID inválido");

            if (!ObjectId.TryParse(userIdClaim, out var userId))
                return (false, "ID de usuario inválido");

            var msg = _db.GetCollection<Message>("messages")
                .Find(Builders<Message>.Filter.Eq(m => m.Id, msgId)).FirstOrDefault();

            if (msg == null) return (false, "Mensaje no encontrado");

            if (msg.Deleted) return (false, "El mensaje ya ha sido eliminado");

            var userActual = _db.GetCollection<ChatUser>("users")
                .Find(Builders<ChatUser>.Filter.Eq(u => u.Id, userId)).FirstOrDefault();

            var esModerador = userActual?.Role == "Moderator";

            var tiempoLimite = TimeSpan.FromMinutes(2);
            var ahora = DateTime.UtcNow;
            var transcurso = ahora - msg.CreatedAt;

            if (userActual == null || !userActual.IsActive) return (false, "Usuario baneado. No puede eliminar mensajes");

            if (!esModerador && transcurso > tiempoLimite)
                return (false, "Límite de 2 minutos superado");

            bool esRemitente = msg.SenderId == userId;

            if (!esRemitente && !esModerador)
                return (false, "Sin permisos");

            _db.GetCollection<Message>("messages")
                .UpdateOne(Builders<Message>.Filter.Eq(m => m.Id, msgId),
                    Builders<Message>.Update
                        .Set(m => m.Deleted, true)
                        .Set(m => m.Content, "[Mensaje eliminado]"));

            return (true, "");
        }

        public (bool success, string error, Report reporte) Denunciar(string reporterId, string targetId, string? messageId, string reason)
        {
            if (!ObjectId.TryParse(reporterId, out var reporterObjId) ||
                !ObjectId.TryParse(targetId, out var targetObjId))
                return (false, "IDs inválidos", null);

            var reporter = _db.GetCollection<ChatUser>("users")
                .Find(Builders<ChatUser>.Filter.Eq(u => u.Id, reporterObjId)).FirstOrDefault();

            if (reporter == null || !reporter.IsActive) return (false, "Usuario baneado. No puede denunciar", null);

            var usuarioObjetivo = _db.GetCollection<ChatUser>("users")
                .Find(Builders<ChatUser>.Filter.Eq(u => u.Id, targetObjId)).FirstOrDefault();

            if (usuarioObjetivo == null) return (false, "Usuario objetivo no encontrado", null);

            if (usuarioObjetivo.Id == reporter.Id) return (false, "No puede denunciarse a uno mismo", null);

            var reporte = new Report
            {
                ReporterId = reporter.Id,
                TargetId = targetObjId,
                Reason = reason ?? "Sin especificar",
                Status = "pending",
                CreatedAt = DateTime.UtcNow
            };

            if (!string.IsNullOrEmpty(messageId))
            {
                if (!ObjectId.TryParse(messageId, out var msgId))
                    return (false, "messageId inválido", null);

                var msgExists = _db.GetCollection<Message>("messages")
                    .Find(Builders<Message>.Filter.Eq(m => m.Id, msgId)).FirstOrDefault();

                if (msgExists == null) return (false, "Mensaje referenced no encontrado", null);

                reporte.MessageId = msgId;
            }

            _db.GetCollection<Report>("reports").InsertOne(reporte);
            return (true, "", reporte);
        }

        public bool ResolverDenuncia(string reporteId, string moderadorId, string accion)
        {
            if (!ObjectId.TryParse(reporteId, out var reporteIdObj) ||
                !ObjectId.TryParse(moderadorId, out var modId))
                return false;

            var reporte = _db.GetCollection<Report>("reports")
                .Find(Builders<Report>.Filter.Eq(r => r.Id, reporteIdObj)).FirstOrDefault();

            if (reporte == null || reporte.Status != "pending") return false;

            var moderador = _db.GetCollection<ChatUser>("users")
                .Find(Builders<ChatUser>.Filter.Eq(u => u.Id, modId)).FirstOrDefault();

            if (moderador == null || moderador.Role != "Moderator") return false;

            if (accion != "ban" && accion != "eliminar_mensaje") return false;

            if (accion == "eliminar_mensaje" && !reporte.MessageId.HasValue) return false;

            if (accion == "ban")
            {
                if (moderador.Id == reporte.TargetId) return false;

                var usuarioObjetivo = _db.GetCollection<ChatUser>("users")
                    .Find(Builders<ChatUser>.Filter.Eq(u => u.Id, reporte.TargetId)).FirstOrDefault();

                if (usuarioObjetivo == null || usuarioObjetivo.Role == "Moderator") return false;
            }

            var ahora = DateTime.UtcNow;

            _db.GetCollection<Report>("reports")
                .UpdateOne(Builders<Report>.Filter.Eq(r => r.Id, reporteIdObj),
                    Builders<Report>.Update
                        .Set(r => r.Status, "resolved")
                        .Set(r => r.ResolvedBy, modId)
                        .Set(r => r.ResolvedAt, ahora));

            if (accion == "ban")
            {
                _db.GetCollection<ChatUser>("users")
                    .UpdateOne(u => u.Id == reporte.TargetId,
                        Builders<ChatUser>.Update
                            .Set(u => u.IsActive, false)
                            .Set(u => u.BannedAt, ahora));
            }

            if (accion == "eliminar_mensaje" && reporte.MessageId.HasValue)
            {
                _db.GetCollection<Message>("messages")
                    .UpdateOne(Builders<Message>.Filter.Eq(m => m.Id, reporte.MessageId.Value),
                        Builders<Message>.Update
                            .Set(m => m.Deleted, true)
                            .Set(m => m.Content, "[Eliminado por mod]"));
            }

            return true;
        }

        public bool Seguir(string userId, string targetId)
        {
            if (userId.Equals(targetId)) return false;

            if (!ObjectId.TryParse(userId, out var userIdObj) ||
                !ObjectId.TryParse(targetId, out var targetIdObj)) return false;

            if (userIdObj == targetIdObj) return false;

            var usuario = _db.GetCollection<ChatUser>("users")
                .Find(Builders<ChatUser>.Filter.Eq(u => u.Id, userIdObj)).FirstOrDefault();

            if (usuario == null || !usuario.IsActive) return false;

            _db.GetCollection<ChatUser>("users")
                .UpdateOne(Builders<ChatUser>.Filter.Eq(u => u.Id, userIdObj),
                    Builders<ChatUser>.Update.AddToSet(f => f.Following, targetIdObj));

            _db.GetCollection<ChatUser>("users")
                .UpdateOne(Builders<ChatUser>.Filter.Eq(u => u.Id, targetIdObj),
                    Builders<ChatUser>.Update.AddToSet(f => f.Followers, userIdObj));

            return true;
        }

        public bool YaSigue(string userId, string targetId)
        {
            if (!ObjectId.TryParse(userId, out var userIdObj) ||
                !ObjectId.TryParse(targetId, out var targetIdObj)) return false;

            if (userIdObj == targetIdObj) return false;

            var targetUser = _db.GetCollection<ChatUser>("users")
                .Find(Builders<ChatUser>.Filter.Eq(u => u.Id, targetIdObj)).FirstOrDefault();

            if (targetUser == null) return false;

            return targetUser.Following?.Any(f => f == userIdObj) ?? false;
        }

        public bool DejarDeSeguir(string userId, string targetId)
        {
            if (!ObjectId.TryParse(userId, out var userIdObj) ||
                !ObjectId.TryParse(targetId, out var targetIdObj)) return false;

            _db.GetCollection<ChatUser>("users")
                .UpdateOne(Builders<ChatUser>.Filter.Eq(u => u.Id, userIdObj),
                    Builders<ChatUser>.Update.Pull(f => f.Following, targetIdObj));

            _db.GetCollection<ChatUser>("users")
                .UpdateOne(Builders<ChatUser>.Filter.Eq(u => u.Id, targetIdObj),
                    Builders<ChatUser>.Update.Pull(f => f.Followers, userIdObj));

            return true;
        }

        public List<ChatUser> UsuariosQueSigo(string userId)
        {
            if (!ObjectId.TryParse(userId, out var userIdObj)) return new List<ChatUser>();

            var filter = Builders<ChatUser>.Filter.AnyEq(u => u.Followers, userIdObj);

            return _db.GetCollection<ChatUser>("users")
                .Find(filter)
                .ToList()
                .Select(u => new ChatUser
                {
                    Id = u.Id,
                    Username = u.Username,
                    Role = u.Role
                })
                .ToList();
        }

        public List<Message> MensajesDirectos(string userId)
        {
            if (!ObjectId.TryParse(userId, out var userIdObj)) return new List<Message>();

            var filter = Builders<Message>.Filter.Or(
                Builders<Message>.Filter.Eq(m => m.ReceiverId, userIdObj) & Builders<Message>.Filter.Eq(m => m.DeliveryStatus, "direct"),
                Builders<Message>.Filter.Eq(m => m.SenderId, userIdObj) & Builders<Message>.Filter.Eq(m => m.DeliveryStatus, "direct"));

            return _db.GetCollection<Message>("messages")
                .Find(filter)
                .SortByDescending(m => m.CreatedAt)
                .ToList();
        }

        public List<Message> SolicitudesPendientes(string userId)
        {
            if (!ObjectId.TryParse(userId, out var userIdObj)) return new List<Message>();

            var filter = Builders<Message>.Filter.And(
                Builders<Message>.Filter.Eq(m => m.ReceiverId, userIdObj),
                Builders<Message>.Filter.Eq(m => m.DeliveryStatus, "request"),
                Builders<Message>.Filter.Eq(m => m.Read, false));

            return _db.GetCollection<Message>("messages")
                .Find(filter)
                .ToList();
        }

        public int ContarSolicitudes(string userId)
        {
            if (!ObjectId.TryParse(userId, out var userIdObj)) return 0;
            var filter = Builders<Message>.Filter.And(
                Builders<Message>.Filter.Eq(m => m.ReceiverId, userIdObj),
                Builders<Message>.Filter.Eq(m => m.DeliveryStatus, "request"),
                Builders<Message>.Filter.Eq(m => m.Read, false));

            return (int)_db.GetCollection<Message>("messages")
                .CountDocuments(filter);
        }
    }
}