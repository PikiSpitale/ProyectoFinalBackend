# Estado del proyecto

Última actualización: 2026-11-03

## Privacidad e invitaciones a equipos (2026-11-03)

- Equipos públicos por defecto, también al leer documentos históricos. El capitán
  puede seleccionar Público/Privado al crear o editar sin alterar integrantes.
- Ingreso directo bloqueado en equipos privados desde la API; listado y detalle
  conservan visibilidad. Los permisos se obtienen del JWT y validan en backend.
- Invitaciones con vigencia de siete días, búsqueda segura de usuarios, historial
  enviado y bandeja recibida en `/team-invitations`; aceptar/rechazar/cancelar con
  controles de estado y mensajes de error/carga/confirmación.
- Transacciones de MongoDB unen aceptación y membresía. Índices únicos impiden
  invitaciones pendientes duplicadas y pertenencia a varios equipos. Creación,
  ingreso, edición, expulsión y abandono también respetan transacciones para
  evitar pérdida de miembros ante escrituras simultáneas.
- No se agregaron dependencias de aplicación. Pruebas backend ejecutables en
  `Backend/TeamTests`; pruebas frontend en `Frontend/tests`.
- Contrato, decisiones operativas y prueba manual en
  [EQUIPOS-INVITACIONES.md](EQUIPOS-INVITACIONES.md).

Verificación: backend compila (advertencia preexistente de nulabilidad en
`TournamentService.cs:94`); 43 comprobaciones backend contra MongoDB 8.0.34
local con replica set y base aleatoria descartable; 11 pruebas frontend y build
correctos. Incluye rollback después de fallo parcial, aceptación concurrente,
aceptaciones cruzadas e ingreso directo, creación contra ingreso y edición contra
alta de miembro. Revisión independiente detectó un defecto de normalización de ObjectId; se
normalizaron los IDs de entrada y se agregaron regresiones de mayúsculas para
destinatario, actor y equipo. ESLint de archivos nuevos y flujos modificados correcto salvo
los tres problemas preexistentes en `MainLayout.jsx` y `TeamsPage.jsx`, confirmados
contra HEAD. No se ejecutó prueba manual de navegador con sesión real ni se
contactó Atlas. El recheck independiente del defecto de ObjectId pasó las 43 comprobaciones.
La prueba manual con sesión real queda como comprobación de integración visual.

## Corrección del registro e inicio de sesión con Google (2026-10-28)

- El middleware recibe la respuesta de Google en
  `https://localhost:7039/api/auth/external/google/callback`. Esta URL se conserva
  para no exigir un cambio de callback en Google Cloud.
- Después de procesar Google, redirige a `/api/auth/external/google/complete`,
  donde el controlador busca o crea el usuario y emite el JWT propio en
  `access_token` (`HttpOnly`, `Secure`, `SameSite=None`, siete días).
- El destino final es fijo: `http://localhost:5173/dashboard`. No se aceptan
  destinos arbitrarios mediante `returnUrl`.
- Se elimina la configuración de `ExternalAuthCookie` del esquema de Identity
  que no se utiliza. La cookie temporal activa sigue siendo `ExternalAuth`,
  correspondiente al esquema `Cookies`.
- `GET /api/auth/me` obtiene el ID desde los claims del JWT y devuelve únicamente
  el DTO del usuario activo. No devuelve tokens ni hashes y no permite cache.
- React verifica la sesión con la API antes de mostrar rutas públicas o privadas.
  Recupera los datos de interfaz tras volver de Google o recargar la página;
  un 401 limpia los datos locales. Los fallos de conexión muestran un reintento.
- La URL del botón de Google se deriva de la configuración de la API.
- Se corrigen dos imports de Rankings que impedían compilar el frontend.

Validación realizada: `dotnet build` y `npm run build` correctos; ESLint correcto
en los archivos del flujo de autenticación modificados; cinco pruebas de sesión
con `node --test tests/session.test.mjs`. Además se ejecutaron ocho comprobaciones
aisladas del controlador/servicio con repositorio y autenticación externa simulados
(ruta final, fallo externo, creación, cookie segura, reutilización, sesión, cuenta
inactiva y requisito de autorización). Estas comprobaciones no contactan Google
ni MongoDB. El backend conserva una advertencia de nulabilidad preexistente en
`TournamentService.cs:94`.

### Verificación del flujo en el navegador

1. Iniciar la API con el perfil HTTPS (`dotnet run --launch-profile https`) y
   el frontend en `http://localhost:5173`.
2. Tener configurados los secretos locales de Google y JWT, MongoDB y el
   certificado HTTPS de desarrollo. El URI autorizado en Google debe coincidir
   exactamente con el callback indicado arriba.
3. Sin datos previos en localStorage, registrarse con Google. Debe pasar por
   el callback y luego por `complete`; `/api/auth/me` debe responder 200 y
   el dashboard debe permanecer visible.
4. Recargar la página y repetir el acceso con el mismo email: debe recuperar
   la sesión y reutilizar el usuario existente.
5. Cerrar sesión y abrir `/dashboard`: debe volver a `/login`. Repetir el acceso
   mediante email/contraseña para verificar el flujo tradicional.

La autenticación real con Google y las políticas del navegador requieren esta
prueba manual. El checklist que sigue conserva el estado histórico de septiembre.

## Objetivo

Desarrollar una plataforma web para gestionar torneos de videojuegos. El
sistema debe permitir administrar usuarios, perfiles, videojuegos, equipos,
torneos, inscripciones, partidos, rankings, notificaciones, puntos y
recompensas.

## Tecnologías definidas

- Backend: ASP.NET Core Web API sobre .NET 10.
- Base de datos: MongoDB Atlas.
- Driver de base de datos: `MongoDB.Driver`.
- Frontend: React + Vite.
- Autenticación: JWT, con preferencia por cookies seguras.

## Estado actual

### Documentación

- [x] Propuesta de backend documentada.
- [x] Requisitos iniciales del frontend documentados.
- [x] Endpoints funcionales propuestos para las principales funcionalidades.
- [x] Reglas iniciales de puntos y recompensas documentadas.
- [x] `AGENTS.md` creado con las reglas de trabajo del repositorio.

### Frontend

- [x] Proyecto React + Vite creado.
- [ ] Landing page completa.
- [ ] Autenticación e integración con la API.
- [ ] Secciones de equipos, foro, torneos y perfil.
- [ ] Visualización de recompensas.

### Backend

- [x] Proyecto ASP.NET Core creado en `Backend/ProyectAPI/`.
- [x] Target framework configurado como `net10.0`.
- [x] OpenAPI inicial configurado.
- [x] Endpoint técnico `/health/mongodb` para verificar Atlas.
- [x] Paquete `MongoDB.Driver` instalado.
- [x] Paquete `BCrypt.Net-Next` instalado para hashear contraseñas.
- [x] User Secrets inicializado y configurado para MongoDB Atlas.
- [x] Conectar la API con MongoDB Atlas.
- [x] Verificar la conexión mediante `/health/mongodb`.
- [x] Crear el modelo inicial de usuarios.
- [x] Crear el repositorio de usuarios con índices únicos para email y username.
- [x] Crear el servicio de registro con hash BCrypt.
- [x] Crear `POST /api/auth/register`.
- [ ] Autenticación y autorización JWT.
- [ ] Endpoints de usuarios y perfiles.
- [ ] Endpoints de equipos e invitaciones.
- [ ] Endpoints de torneos e inscripciones.
- [ ] Brackets, partidos y moderación.
- [ ] Rankings.
- [ ] Notificaciones.
- [ ] Puntos y recompensas automáticas.

## Decisiones tomadas

1. Se utilizará .NET 10 en lugar de .NET 8 porque es la versión definida para
   este proyecto.
2. Se utilizará MongoDB Atlas en lugar de SQL Server y Entity Framework Core.
3. La arquitectura mantendrá una separación por Controllers, Services,
   Repositories, Models y DTOs, tomando como referencia el proyecto
   `CinedexAPI`.
4. El bracket se generará automáticamente, pero el avance de los ganadores
   será gestionado manualmente por un moderador.
5. Los puntos y recompensas se asignarán exclusivamente desde el backend.
6. Las operaciones de puntos y recompensas deberán ser idempotentes.

## Tarea actual

Completar y probar el registro de usuarios mediante `POST /api/auth/register`.
El registro guarda únicamente el hash BCrypt de la contraseña y no devuelve
credenciales ni hashes en la respuesta.

Esta etapa todavía no incluye login ni JWT.

## Próximos pasos de infraestructura

1. Probar el registro desde Swagger con un usuario de prueba.
2. Verificar en Atlas que se creó la colección `users`.
3. Confirmar que la contraseña almacenada es un hash y no texto plano.
4. Implementar login y JWT.

## Etapas posteriores

Una vez verificada la conexión, se podrá avanzar con:

1. Modelos y colecciones del dominio.
2. Usuarios y perfiles.
3. Registro y login seguro con JWT.
4. Equipos, torneos e inscripciones.

## Referencias

- `Documentacion/BACKEND.md`
- `Documentacion/REQUISITOS-FRONT.md`
- `Documentacion/Proyecto final Agustin Spitale Mateo Escobar.pdf`
- Repositorio de referencia: `CinedexAPI`
