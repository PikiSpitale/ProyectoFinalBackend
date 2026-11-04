# Privacidad e invitaciones a equipos

## Contrato y reglas

`isPrivate` es un booleano: `false` significa público, `true` significa ingreso
por invitación. Los documentos existentes sin el campo se interpretan como
públicos. La privacidad no oculta equipos ni integrantes y cambiarla no altera
la membresía. Solo el capitán actual puede editar el equipo, buscar candidatos,
enviar invitaciones y cancelarlas. El usuario autenticado se obtiene del JWT.

Las invitaciones duran siete días desde su creación en UTC. Estados:
`Pending`, `Accepted`, `Rejected`, `Cancelled`, `Expired`. Solo una invitación
pendiente y vigente se puede aceptar, rechazar o cancelar. Los estados terminales
no se reutilizan; el capitán puede enviar una nueva invitación tras su cierre.
El destinatario debe existir, estar activo, no pertenecer a ningún equipo y
tener asociado el videojuego con nombre dentro del juego y rango. Se valida
tanto al invitar como al aceptar. No se admiten invitaciones al propio capitán
ni a miembros actuales.

## Endpoints

Todas las rutas tienen prefijo `/api`. Los GET de listado y detalle conservan
la consulta pública; las demás operaciones requieren autenticación.

| Método | Ruta | Actor / resultado |
| --- | --- | --- |
| GET | `/teams` | Lista con `isPrivate` |
| GET | `/teams/{id}` | Detalle e integrantes con `isPrivate` |
| POST | `/teams` | Usuario sin equipo; acepta `isPrivate` opcional (false) |
| PUT | `/teams/{id}` | Capitán; acepta `isPrivate` opcional |
| POST | `/teams/{id}/join` | Solo equipo público y usuario elegible |
| GET | `/teams/{id}/invitees?username=texto` | Capitán; búsqueda limitada, sin email ni credenciales |
| POST | `/teams/{id}/invitations` | Capitán; cuerpo `{ "recipientId": "..." }` |
| GET | `/teams/{id}/invitations` | Capitán; historial de invitaciones enviadas |
| GET | `/team-invitations` | Destinatario; invitaciones recibidas |
| POST | `/team-invitations/{id}/accept` | Destinatario; acepta y agrega al equipo |
| POST | `/team-invitations/{id}/reject` | Destinatario; rechaza |
| POST | `/team-invitations/{id}/cancel` | Capitán actual; cancela |

La búsqueda admite entre 2 y 40 caracteres, busca texto literal sin distinguir
mayúsculas y limita a 20 usuarios activos ordenados por nombre.
La búsqueda devuelve únicamente `id`, `username`, `avatarUrl`. Las invitaciones
devuelven `id`, `teamId`, `teamName`, `senderId`, `senderUsername`, `recipientId`,
`recipientUsername`, `status`, `createdAt`, `expiresAt`.

Errores con cuerpo `{ "message": "..." }`: 400 para formato inválido, 401 sin
sesión válida, 403 por permisos o ingreso directo privado, 404 si no existe el
recurso, 409 por conflicto de membresía, duplicación o estado terminal y 410
por vencimiento al resolver una invitación.

## Consistencia y operación

Las escrituras de equipos e invitaciones usan sesiones y transacciones de
MongoDB. Aceptar modifica la invitación y el equipo en un mismo commit; un fallo
antes del commit revierte ambas escrituras. Un reintento de una invitación ya
aceptada devuelve conflicto y no vuelve a agregar al usuario. El índice único
de integrantes evita pertenencia cruzada incluso entre creación, ingreso
directo y aceptación simultáneos. Las demás modificaciones de equipo también
deben respetar la transacción para evitar reemplazos que borren altas concurrentes.

Se necesita MongoDB Atlas o un replica set local con soporte de transacciones.
No hay alternativa silenciosa sin transacciones. Si datos históricos violan la
unicidad, se debe investigar y corregir expresamente antes de crear el índice;
la aplicación no elimina miembros automáticamente. El vencimiento conserva el
historial: no se usa TTL para borrar invitaciones.

Referencia técnica: [transacciones del driver oficial C#](https://www.mongodb.com/docs/drivers/csharp/current/crud/transactions/).

## Prueba manual

1. Preparar dos capitanes y un destinatario con el mismo videojuego, nombre
   dentro del juego y rango. El destinatario comienza sin equipo.
2. Crear un equipo privado y otro público. Verificar etiquetas y visibilidad.
3. Intentar `/join` del privado desde la API: debe denegar el ingreso.
4. Como capitán, buscar al destinatario e invitarlo. Repetir: debe devolver
   conflicto mientras exista una pendiente vigente.
5. Como un tercer usuario, intentar aceptar/rechazar/cancelar: debe denegarse.
6. Como destinatario, abrir Invitaciones, aceptar y comprobar que aparece una
   sola vez en el equipo. Repetir aceptación: conflicto sin duplicación.
7. Probar rechazo, cancelación y vencimiento con usuarios de prueba; comprobar
   que no cambian la membresía. No alterar fechas de datos reales.
8. Cambiar privacidad y comprobar que conserva los integrantes.
9. Ejecutar las pruebas aisladas de concurrencia con un replica set de prueba
   para verificar carreras entre aceptaciones y entre aceptación e ingreso.

La entrega no incluye notificaciones por email ni un sistema de solicitudes de
ingreso: el capitán inicia la invitación y el destinatario la gestiona en la app.

## Verificaciones automatizadas

```sh
dotnet build ProyectAPI.csproj --no-restore
# Solo un replica set de prueba: el runner usa una base aleatoria y la elimina.
TEST_MONGODB_URI='mongodb://127.0.0.1:27187/?replicaSet=tup-tests' dotnet run --project tests/TeamTests --no-restore
cd Frontend
node --test tests/*.test.mjs
npm run build
```

Antes del primer uso, restaurar los proyectos .NET. Sin `TEST_MONGODB_URI`, el
runner ejecuta las comprobaciones locales de DTO/defaults e informa explícitamente
que omite la integración. No obtiene cadenas de conexión de los secretos de la app.
`TEST_MONGODB_INITIALIZE=1` es una utilidad exclusiva para iniciar el replica set
local de prueba `tup-tests` en 127.0.0.1:27187; no se usa con Atlas.

Los ObjectId recibidos se normalizan antes de comparar o persistir: usar
mayúsculas en una ruta o destinatario no crea otra identidad, no evade las
restricciones y no oculta invitaciones de la bandeja normal del usuario.
