# Herramientas de prueba

Scripts de humo (smoke tests) y utilidades para validar la API contra una base de
desarrollo. No forman parte del build del backend: se ejecutan a mano.

## Requisitos

- La API corriendo en `https://localhost:7039` (perfil `https`, ver
  `Properties/launchSettings.json`).
- .NET SDK 10 y PowerShell 5.1 o superior.
- Cadena de MongoDB configurada en user secrets del proyecto
  (`MongoDbSettings:ConnectionString`) o en la variable de entorno
  `MongoDbSettings__ConnectionString`.

## Smoke tests

Desde esta carpeta:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\smoke\smoke.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\smoke\smoke-team.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\smoke\smoke-ranking.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\smoke\smoke-rules.ps1
```

| Script | Qué cubre |
| --- | --- |
| `smoke.ps1` | Registro, perfil, juegos, torneos individuales, rango, llaves y resultados. |
| `smoke-team.ps1` | Equipos, invitaciones, roles y inscripción por equipos. |
| `smoke-ranking.ps1` | Ranking web, ledger de puntos, historial y torneos por equipo. |
| `smoke-rules.ps1` | Fechas, formato configurable, creador, plantilla congelada y ranking. |

Los tests crean torneos con inicio a 31 minutos y esperan 65 segundos: la
inscripción se habilita 30 minutos antes del inicio. `smoke.ps1` y
`smoke-team.ps1` dejan usuarios/equipos de prueba; `smoke-ranking.ps1` y
`smoke-rules.ps1` limpian lo que crean.

Variables opcionales:

- `SMOKE_BASE`: URL base de la API (por defecto `https://localhost:7039/api`).
- `SMOKE_DBTOOL`: ruta al `.csproj` de `nexus-dbtool` (por defecto el de esta carpeta).

## nexus-dbtool

Utilidad para inspeccionar y limpiar datos de prueba:

```powershell
dotnet run --project .\nexus-dbtool -- list
dotnet run --project .\nexus-dbtool -- promote <usuario>
dotnet run --project .\nexus-dbtool -- delete-user <usuario>
dotnet run --project .\nexus-dbtool -- delete-team "<nombre>"
dotnet run --project .\nexus-dbtool -- delete-tournament <id>
dotnet run --project .\nexus-dbtool -- set-start <id> -1
```

`set-start <id> <minutos>` mueve la fecha de inicio del torneo; con `-1` sirve
para comprobar que la inscripción se cierra al llegar al inicio.
