$ErrorActionPreference = 'Stop'
$base = if ($env:SMOKE_BASE) { $env:SMOKE_BASE } else { 'https://localhost:7039/api' }
$stamp = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$pw = 'Smoke123!'
$script:pass = 0
$script:fail = 0

function Check([string]$name, [bool]$cond, [string]$detail = '') {
  if ($cond) { $script:pass++; Write-Host "PASS  $name" -ForegroundColor Green }
  else { $script:fail++; Write-Host "FAIL  $name  $detail" -ForegroundColor Red }
}

function New-ApiSession { return New-Object Microsoft.PowerShell.Commands.WebRequestSession }

function Call($sess, [string]$method, [string]$path, $body = $null) {
  $p = @{ Uri = "$base$path"; Method = $method; WebSession = $sess; UseBasicParsing = $true }
  if ($null -ne $body) {
    $p.ContentType = 'application/json; charset=utf-8'
    $p.Body = [Text.Encoding]::UTF8.GetBytes(($body | ConvertTo-Json -Depth 12 -Compress))
  }
  try {
    $r = Invoke-WebRequest @p
    return [pscustomobject]@{ status = [int]$r.StatusCode; body = $r.Content }
  } catch {
    $resp = $_.Exception.Response
    if ($resp) {
      $sr = New-Object IO.StreamReader($resp.GetResponseStream())
      return [pscustomobject]@{ status = [int]$resp.StatusCode; body = $sr.ReadToEnd() }
    }
    throw
  }
}

function Json($r) { try { return ($r.body | ConvertFrom-Json) } catch { return $null } }

function Register-SmokeUser([string]$suffix, [string]$rank) {
  $user = "smoke${stamp}$suffix"
  $sess = New-ApiSession
  $r = Call $sess 'POST' '/auth/register' @{
    username = $user; email = "$user@example.com"; password = $pw; confirmPassword = $pw
    games = @(@{ gameName = 'Valorant'; inGameName = "$user#EUW"; rank = $rank })
  }
  return [pscustomobject]@{ username = $user; session = $sess; register = $r; id = (Json $r).user.id }
}

Write-Host "== Smoke RF-21 inscripción de equipos (stamp $stamp) ==" -ForegroundColor Cyan

# ---------- 7 usuarios con Valorant ----------
$u1 = Register-SmokeUser 't1' 'Platino 1'   # capitán
$u2 = Register-SmokeUser 't2' 'Diamante 1'
$u3 = Register-SmokeUser 't3' 'Platino 2'
$u4 = Register-SmokeUser 't4' 'Ascendente 1'
$u5 = Register-SmokeUser 't5' 'Inmortal 1'
$u6 = Register-SmokeUser 't6' 'Diamante 2'
$u7 = Register-SmokeUser 't7' 'Oro 1'       # inicialmente por debajo del mínimo
$all = @($u1,$u2,$u3,$u4,$u5,$u6,$u7)
$members = @($u2,$u3,$u4,$u5,$u6,$u7)
$regFailures = @($all | Where-Object { $_.register.status -ne 201 })
Check 'registro de 7 usuarios (201)' ($regFailures.Count -eq 0) (($regFailures | ForEach-Object { "$($_.username)=$($_.register.status) $($_.register.body)" }) -join ' | ')

# ---------- Crear equipo (capitán u1) ----------
$teamName = "SmokeTeam $stamp"
$teamCreate = Call $u1.session 'POST' '/teams' @{
  name = $teamName; tag = "T$($stamp % 10000)".Substring(0,4); logoUrl = ''
  gameName = 'Valorant'; joinPolicy = 'InviteOnly'
}
$teamId = (Json $teamCreate).id
Check 'RF-09 crear equipo' ($teamCreate.status -eq 201 -and -not [string]::IsNullOrWhiteSpace($teamId)) "$($teamCreate.status) $($teamCreate.body)"

# ---------- Invitar y aceptar a los 6 restantes ----------
$inviteResults = @()
foreach ($m in $members) {
  $inv = Call $u1.session 'POST' "/teams/$teamId/invitations" @{ username = $m.username }
  $invId = (Json $inv).id
  $acc = Call $m.session 'POST' "/teams/invitations/$invId/respond" @{ response = 'Accept' }
  $inviteResults += "$($m.username)=$($inv.status)/$($acc.status)"
}
$badInvites = @($inviteResults | Where-Object { $_ -notmatch '=200/200$' })
Check 'RF-11/RF-12 invitar y aceptar 6 integrantes' ($badInvites.Count -eq 0) ($inviteResults -join ', ')

$team = Json (Call $u1.session 'GET' "/teams/$teamId")
Check 'equipo con 7 integrantes' (@($team.members).Count -eq 7) "members=$(@($team.members).Count)"

# ---------- 5 titulares + 2 suplentes (capitán ya es titular) ----------
$starterResults = @()
foreach ($m in @($members[4], $members[5])) {
  $r = Call $u1.session 'PUT' "/teams/$teamId/members/$($m.id)" @{ role = 'Member'; isStarter = $false }
  $starterResults += "$($m.username)=$($r.status)"
}
Check 'RF-15 marcar 2 suplentes' (@($starterResults | Where-Object { $_ -notmatch '=200$' }).Count -eq 0) ($starterResults -join ', ')
$teamAfter = Json (Call $u1.session 'GET' "/teams/$teamId")
Check 'equipo con 5 titulares y 2 suplentes' (@($teamAfter.members | Where-Object { $_.isStarter }).Count -eq 5 -and @($teamAfter.members | Where-Object { -not $_.isStarter }).Count -eq 2) "titulares=$(@($teamAfter.members | Where-Object { $_.isStarter }).Count) suplentes=$(@($teamAfter.members | Where-Object { -not $_.isStarter }).Count)"

# ---------- Crear torneo por equipos (moderador) ----------
$tool = if ($env:SMOKE_DBTOOL) { $env:SMOKE_DBTOOL } else { Join-Path $PSScriptRoot '..\nexus-dbtool\nexus-dbtool.csproj' }
& dotnet run --project $tool -- promote $u1.username | Out-Null
$login = Call $u1.session 'POST' '/auth/login' @{ email = "$($u1.username)@example.com"; password = $pw }
Check 'login moderador para crear torneo' ($login.status -eq 200 -and (Json $login).user.role -eq 'Moderator') $login.body

# La inscripción abre 30 minutos antes del inicio; se crea con 31 para poder esperar la apertura.
$start = (Get-Date).ToUniversalTime().AddMinutes(31)
$title = "Smoke Team Cup $stamp"
$createBody = @{
  title = $title; gameName = 'Valorant'; bannerUrl = ''; prizePool = 0; maxParticipants = 8
  rules = 'Reglas smoke equipo'; rankConditions = 'Mínimo: Platino 1'
  startDate = $start.ToString('o'); endDate = $start.AddHours(1).ToString('o'); tournamentType = 'Equipo'
}
$create = Call $u1.session 'POST' '/tournaments' $createBody
$list = Json (Call $u1.session 'GET' '/tournaments')
$t = @($list | Where-Object { $_.title -eq $title })[0]
$tid = $t.id
Check 'crear torneo por equipos' ($create.status -eq 200 -and $null -ne $tid) "$($create.status) $($create.body)"

# La inscripción se habilita 30 minutos antes: esperamos a que abra la ventana.
Write-Host "Esperando la apertura de la inscripción (65 s)..." -ForegroundColor Yellow
Start-Sleep -Seconds 65

# ---------- Solo el capitán puede inscribir ----------
$notCaptain = Call $u2.session 'POST' "/tournaments/$tid/register-team" @{ teamId = $teamId }
Check 'RF-21 solo el capitán inscribe' ($notCaptain.status -eq 400 -and $notCaptain.body -match 'capit') "$($notCaptain.status) $($notCaptain.body)"

# ---------- Integrante por debajo del rango mínimo ----------
$lowRank = Call $u1.session 'POST' "/tournaments/$tid/register-team" @{ teamId = $teamId }
Check 'RF-21 rechaza equipo con integrante bajo el mínimo' ($lowRank.status -eq 400 -and $lowRank.body -match [regex]::Escape($u7.username) -and $lowRank.body -match 'al menos Platino 1') "$($lowRank.status) $($lowRank.body)"

# ---------- Corregir rango del integrante y reintentar ----------
$fixRank = Call $u7.session 'PUT' '/profile/games' @{ gameName = 'Valorant'; inGameName = "$($u7.username)#EUW"; rank = 'Platino 2' }
Check 'RF-07 corregir rango del integrante' ($fixRank.status -eq 200) $fixRank.body

$registerTeam = Call $u1.session 'POST' "/tournaments/$tid/register-team" @{ teamId = $teamId }
Check 'RF-21 inscribir equipo válido' ($registerTeam.status -eq 200) "$($registerTeam.status) $($registerTeam.body)"

$tAfter = Json (Call $u1.session 'GET' "/tournaments/$tid")
$teamParticipant = @($tAfter.participants | Where-Object { $_.teamId -eq $teamId })[0]
Check 'torneo muestra el equipo inscripto' ($null -ne $teamParticipant -and $teamParticipant.teamName -eq $teamName) ($tAfter.participants | ConvertTo-Json -Depth 4)

$dupTeam = Call $u1.session 'POST' "/tournaments/$tid/register-team" @{ teamId = $teamId }
Check 'RF-21 no permite inscribir el mismo equipo dos veces' ($dupTeam.status -eq 400 -and $dupTeam.body -match 'ya está inscripto') "$($dupTeam.status) $($dupTeam.body)"

Write-Host ""
Write-Host "PASS=$($script:pass) FAIL=$($script:fail)" -ForegroundColor Cyan
Write-Host "TEST_USERS=$($all.username -join ',')"
Write-Host "TEST_TOURNAMENT=$tid"
Write-Host "TEST_TEAM=$teamId"
Write-Host "TEST_TEAM_NAME=$teamName"
if ($script:fail -gt 0) { exit 1 }
