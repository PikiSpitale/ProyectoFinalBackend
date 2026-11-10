$ErrorActionPreference = 'Stop'
$base = if ($env:SMOKE_BASE) { $env:SMOKE_BASE } else { 'https://localhost:7039/api' }
$stamp = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$pw = 'Smoke123!'
$script:pass = 0
$script:fail = 0
$tool = if ($env:SMOKE_DBTOOL) { $env:SMOKE_DBTOOL } else { Join-Path $PSScriptRoot '..\nexus-dbtool\nexus-dbtool.csproj' }

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
  $user = "rule${stamp}$suffix"
  $sess = New-ApiSession
  $r = Call $sess 'POST' '/auth/register' @{
    username = $user; email = "$user@example.com"; password = $pw; confirmPassword = $pw
    games = @(@{ gameName = 'Valorant'; inGameName = "$user#EUW"; rank = $rank })
  }
  return [pscustomobject]@{ username = $user; session = $sess; register = $r; id = (Json $r).user.id }
}

function Find-Tournament($sess, [string]$title) {
  $list = Json (Call $sess 'GET' '/tournaments')
  return @($list | Where-Object { $_.title -eq $title })[0]
}

function TournamentBody([string]$title, [string]$game, [string]$type, [datetime]$start, [int]$starters = -1, [int]$subs = -1) {
  $b = @{
    title = $title; gameName = $game; bannerUrl = ''; prizePool = 0; maxParticipants = 8
    rules = 'Reglas smoke'; rankConditions = 'Sin restriccion'
    startDate = $start.ToString('o'); endDate = $start.AddHours(1).ToString('o'); tournamentType = $type
  }
  if ($starters -ge 0) { $b['startersCount'] = $starters }
  if ($subs -ge 0) { $b['substitutesCount'] = $subs }
  return $b
}

function PointsOf($sess, [string]$uid, [string]$game = 'Valorant') {
  $rows = Json (Call $sess 'GET' "/rankings?gameName=$([uri]::EscapeDataString($game))")
  $row = @($rows | Where-Object { $_.userId -eq $uid })[0]
  if ($null -eq $row) { return -1 }
  return [int]$row.points
}

Write-Host "== Smoke nuevas reglas: fechas, formato, creador, plantilla y ranking (stamp $stamp) ==" -ForegroundColor Cyan

# ---------- usuarios ----------
$mod = Register-SmokeUser 'mod' 'Platino 1'          # capitán equipo A y moderador
$p2 = Register-SmokeUser 'p2' 'Diamante 1'
$p3 = Register-SmokeUser 'p3' 'Platino 2'
$p4 = Register-SmokeUser 'p4' 'Ascendente 1'
$p5 = Register-SmokeUser 'p5' 'Inmortal 1'
$p6 = Register-SmokeUser 'p6' 'Diamante 2'           # suplente del equipo A
$q1 = Register-SmokeUser 'q1' 'Platino 3'            # capitán equipo B
$q2 = Register-SmokeUser 'q2' 'Diamante 3'
$q3 = Register-SmokeUser 'q3' 'Ascendente 2'
$q4 = Register-SmokeUser 'q4' 'Inmortal 2'
$q5 = Register-SmokeUser 'q5' 'Platino 1'
$all = @($mod,$p2,$p3,$p4,$p5,$p6,$q1,$q2,$q3,$q4,$q5)
$badReg = @($all | Where-Object { $_.register.status -ne 201 })
Check 'registro de 11 usuarios' ($badReg.Count -eq 0) (($badReg | ForEach-Object { "$($_.username)=$($_.register.status) $($_.register.body)" }) -join ' | ')

& dotnet run --project $tool -- promote $mod.username | Out-Null
$login = Call $mod.session 'POST' '/auth/login' @{ email = "$($mod.username)@example.com"; password = $pw }
Check 'login moderador' ($login.status -eq 200 -and (Json $login).user.role -eq 'Moderator') $login.body

# ---------- validaciones de fechas ----------
$s = (Get-Date).ToUniversalTime()
$pastBody = TournamentBody "Rules Past $stamp" 'Valorant' 'Individual' $s.AddMinutes(-5)
$pastBody.endDate = $s.AddHours(1).ToString('o')
$past = Call $mod.session 'POST' '/tournaments' $pastBody
Check 'fechas: inicio en el pasado rechazado' ($past.status -eq 400 -and $past.body -match '30 minutos') "$($past.status) $($past.body)"

$s = (Get-Date).ToUniversalTime().AddMinutes(31)
$badEndBody = TournamentBody "Rules BadEnd $stamp" 'Valorant' 'Individual' $s
$badEndBody.endDate = $s.AddMinutes(-5).ToString('o')
$badEnd = Call $mod.session 'POST' '/tournaments' $badEndBody
Check 'fechas: fin anterior al inicio rechazado' ($badEnd.status -eq 400 -and $badEnd.body -match 'anterior a la fecha de inicio') "$($badEnd.status) $($badEnd.body)"

$s = (Get-Date).ToUniversalTime().AddMinutes(10)
$soon = Call $mod.session 'POST' '/tournaments' (TournamentBody "Rules Soon $stamp" 'Valorant' 'Individual' $s)
Check 'fechas: inicio con menos de 30 minutos rechazado' ($soon.status -eq 400 -and $soon.body -match '30 minutos') "$($soon.status) $($soon.body)"

$s = (Get-Date).ToUniversalTime().AddMinutes(31)
$okDateTitle = "Rules Dates $stamp"
$okDate = Call $mod.session 'POST' '/tournaments' (TournamentBody $okDateTitle 'Valorant' 'Individual' $s)
Check 'fechas: crear con 31 minutos permitido' ($okDate.status -eq 200) "$($okDate.status) $($okDate.body)"

# ---------- formato de equipo configurable ----------
$s = (Get-Date).ToUniversalTime().AddMinutes(31)
$eaTitle = "Rules EA $stamp"
$eaCreate = Call $mod.session 'POST' '/tournaments' (TournamentBody $eaTitle 'EA FC 27' 'Equipo' $s 6 3)
$ea = Find-Tournament $mod.session $eaTitle
Check 'formato EA FC 6+3 configurable' ($eaCreate.status -eq 200 -and $ea.startersCount -eq 6 -and $ea.substitutesCount -eq 3) "$($eaCreate.status) $($eaCreate.body) => $($ea.startersCount)/$($ea.substitutesCount)"

$eaDefTitle = "Rules EADef $stamp"
$eaDefCreate = Call $mod.session 'POST' '/tournaments' (TournamentBody $eaDefTitle 'EA FC 27' 'Equipo' $s)
$eaDef = Find-Tournament $mod.session $eaDefTitle
Check 'formato EA FC por defecto 11+7' ($eaDefCreate.status -eq 200 -and $eaDef.startersCount -eq 11 -and $eaDef.substitutesCount -eq 7) "$($eaDefCreate.status) $($eaDefCreate.body) => $($eaDef.startersCount)/$($eaDef.substitutesCount)"

$rocketBad = Call $mod.session 'POST' '/tournaments' (TournamentBody "Rules RocketBad $stamp" 'Rocket League' 'Equipo' $s 4 0)
Check 'formato Rocket League maximo 3 titulares' ($rocketBad.status -eq 400 -and $rocketBad.body -match 'Rocket League') "$($rocketBad.status) $($rocketBad.body)"

$rocketTitle = "Rules Rocket $stamp"
$rocketCreate = Call $mod.session 'POST' '/tournaments' (TournamentBody $rocketTitle 'Rocket League' 'Equipo' $s 2 0)
$rocket = Find-Tournament $mod.session $rocketTitle
Check 'formato Rocket League 2v2' ($rocketCreate.status -eq 200 -and $rocket.startersCount -eq 2 -and $rocket.substitutesCount -eq 0) "$($rocketCreate.status) $($rocketCreate.body) => $($rocket.startersCount)/$($rocket.substitutesCount)"

$valBad = Call $mod.session 'POST' '/tournaments' (TournamentBody "Rules ValBad $stamp" 'Valorant' 'Equipo' $s 6 2)
Check 'formato Valorant exige 5 titulares' ($valBad.status -eq 400 -and $valBad.body -match 'exactamente 5') "$($valBad.status) $($valBad.body)"

$csTitle = "Rules CS $stamp"
$csCreate = Call $mod.session 'POST' '/tournaments' (TournamentBody $csTitle 'Counter-Strike 2' 'Equipo' $s)
$cs = Find-Tournament $mod.session $csTitle
Check 'formato CS2 por defecto 5+2' ($csCreate.status -eq 200 -and $cs.startersCount -eq 5 -and $cs.substitutesCount -eq 2) "$($csCreate.status) $($csCreate.body) => $($cs.startersCount)/$($cs.substitutesCount)"

$lolTitle = "Rules LoL $stamp"
$lolCreate = Call $mod.session 'POST' '/tournaments' (TournamentBody $lolTitle 'League of Legends' 'Equipo' $s)
$lol = Find-Tournament $mod.session $lolTitle
Check 'formato LoL por defecto 5+2' ($lolCreate.status -eq 200 -and $lol.startersCount -eq 5 -and $lol.substitutesCount -eq 2) "$($lolCreate.status) $($lolCreate.body) => $($lol.startersCount)/$($lol.substitutesCount)"

# ---------- torneo cuya inscripción todavía no abrió ----------
$s45 = (Get-Date).ToUniversalTime().AddMinutes(45)
$notOpenTitle = "Rules NotOpen $stamp"
$notOpenCreate = Call $mod.session 'POST' '/tournaments' (TournamentBody $notOpenTitle 'Valorant' 'Individual' $s45)
$notOpen = Find-Tournament $mod.session $notOpenTitle
Check 'crear torneo con ventana futura' ($notOpenCreate.status -eq 200 -and $null -ne $notOpen) "$($notOpenCreate.status) $($notOpenCreate.body)"

# ---------- equipos ----------
$teamAName = "Rules Team A $stamp"
$teamBName = "Rules Team B $stamp"
$teamA = Call $mod.session 'POST' '/teams' @{ name = $teamAName; tag = "RA$($stamp % 1000)"; logoUrl = ''; gameName = 'Valorant'; joinPolicy = 'InviteOnly' }
$teamB = Call $q1.session 'POST' '/teams' @{ name = $teamBName; tag = "RB$($stamp % 1000)"; logoUrl = ''; gameName = 'Valorant'; joinPolicy = 'InviteOnly' }
$teamAId = (Json $teamA).id
$teamBId = (Json $teamB).id
Check 'crear equipos A y B' ($teamA.status -eq 201 -and $teamB.status -eq 201 -and $teamAId -and $teamBId) "$($teamA.status)/$($teamB.status) $($teamA.body) $($teamB.body)"

$invites = @()
foreach ($m in @($p2,$p3,$p4,$p5,$p6)) {
  $inv = Call $mod.session 'POST' "/teams/$teamAId/invitations" @{ username = $m.username }
  $acc = Call $m.session 'POST' "/teams/invitations/$((Json $inv).id)/respond" @{ response = 'Accept' }
  $invites += "$($m.username)=$($inv.status)/$($acc.status)"
}
foreach ($m in @($q2,$q3,$q4,$q5)) {
  $inv = Call $q1.session 'POST' "/teams/$teamBId/invitations" @{ username = $m.username }
  $acc = Call $m.session 'POST' "/teams/invitations/$((Json $inv).id)/respond" @{ response = 'Accept' }
  $invites += "$($m.username)=$($inv.status)/$($acc.status)"
}
Check 'invitar y aceptar integrantes' (@($invites | Where-Object { $_ -notmatch '=200/200$' }).Count -eq 0) ($invites -join ', ')

# Equipo A: 5 titulares (mod, p2..p5) + 1 suplente (p6)
$sub = Call $mod.session 'PUT' "/teams/$teamAId/members/$($p6.id)" @{ role = 'Member'; isStarter = $false }
Check 'marcar suplente en equipo A' ($sub.status -eq 200) $sub.body

# ---------- torneo principal por equipos ----------
$mainStart = (Get-Date).ToUniversalTime().AddMinutes(31)
$mainTitle = "Rules Main $stamp"
$mainCreate = Call $mod.session 'POST' '/tournaments' (TournamentBody $mainTitle 'Valorant' 'Equipo' $mainStart)
$tMain = Find-Tournament $mod.session $mainTitle
$tidMain = $tMain.id
Check 'crear torneo principal por equipos' ($mainCreate.status -eq 200 -and $null -ne $tidMain) "$($mainCreate.status) $($mainCreate.body)"

# ---------- creador visible solo para staff ----------
$modView = Json (Call $mod.session 'GET' "/tournaments/$tidMain")
$userView = Json (Call $p2.session 'GET' "/tournaments/$tidMain")
Check 'creador visible para moderador' ($modView.createdByUsername -eq $mod.username) "$($modView.createdByUsername)"
Check 'creador oculto para usuario comun (detalle)' ($null -eq $userView.createdByUsername) "$($userView.createdByUsername)"

$modList = Json (Call $mod.session 'GET' '/tournaments')
$userList = Json (Call $p2.session 'GET' '/tournaments')
$modRow = @($modList | Where-Object { $_.id -eq $tidMain })[0]
$userRow = @($userList | Where-Object { $_.id -eq $tidMain })[0]
Check 'creador visible para moderador (listado)' ($modRow.createdByUsername -eq $mod.username) "$($modRow.createdByUsername)"
Check 'creador oculto para usuario comun (listado)' ($null -eq $userRow.createdByUsername) "$($userRow.createdByUsername)"

# ---------- edición: fechas y formato ----------
$updTitle = Call $mod.session 'PUT' "/tournaments/$tidMain" @{ title = "$mainTitle upd" }
Check 'editar titulo sin tocar fechas' ($updTitle.status -eq 200 -and (Json $updTitle).title -eq "$mainTitle upd") $updTitle.body

$badNewStart = (Get-Date).ToUniversalTime().AddMinutes(5)
$updBadStart = Call $mod.session 'PUT' "/tournaments/$tidMain" @{ startDate = $badNewStart.ToString('o') }
Check 'editar inicio con menos de 30 minutos rechazado' ($updBadStart.status -eq 400 -and $updBadStart.body -match '30 minutos') "$($updBadStart.status) $($updBadStart.body)"

$sameStart = (Json (Call $mod.session 'GET' "/tournaments/$tidMain")).startDate
$updSameStart = Call $mod.session 'PUT' "/tournaments/$tidMain" @{ startDate = $sameStart; rules = 'Reglas actualizadas' }
Check 'editar sin cambiar la fecha no revalida' ($updSameStart.status -eq 200) "$($updSameStart.status) $($updSameStart.body)"

# ---------- esperar apertura de inscripción ----------
Write-Host "Esperando la apertura de la inscripción (65 s)..." -ForegroundColor Yellow
Start-Sleep -Seconds 65

# ---------- ventana de inscripción ----------
$tooEarly = Call $p2.session 'POST' "/tournaments/$($notOpen.id)/register" @{ userId = $p2.id; username = $p2.username }
Check 'inscripción antes de la ventana rechazada' ($tooEarly.status -eq 400 -and $tooEarly.body -match '30 minutos antes') "$($tooEarly.status) $($tooEarly.body)"

& dotnet run --project $tool -- set-start $($notOpen.id) -1 | Out-Null
$closed = Call $p2.session 'POST' "/tournaments/$($notOpen.id)/register" @{ userId = $p2.id; username = $p2.username }
Check 'inscripción cerrada al llegar al inicio' ($closed.status -eq 400 -and $closed.body -match 'se cerr') "$($closed.status) $($closed.body)"

# ---------- inscripción de equipos ----------
$regA = Call $mod.session 'POST' "/tournaments/$tidMain/register-team" @{ teamId = $teamAId }
$regB = Call $q1.session 'POST' "/tournaments/$tidMain/register-team" @{ teamId = $teamBId }
Check 'inscribir equipos A y B' ($regA.status -eq 200 -and $regB.status -eq 200) "$($regA.status)/$($regB.status) $($regA.body) $($regB.body)"

$formatChange = Call $mod.session 'PUT' "/tournaments/$tidMain" @{ startersCount = 5; substitutesCount = 1 }
Check 'no cambiar formato con inscriptos' ($formatChange.status -eq 400 -and $formatChange.body -match 'formato de equipo cuando ya hay inscritos') "$($formatChange.status) $($formatChange.body)"

# ---------- iniciar antes de la fecha (solo manual) y bloquear plantilla ----------
$bracket = Call $mod.session 'POST' "/tournaments/$tidMain/bracket"
$tInProgress = Json (Call $mod.session 'GET' "/tournaments/$tidMain")
Check 'iniciar torneo manualmente antes de la fecha' ($bracket.status -eq 200 -and $tInProgress.status -eq 'En curso') "$($bracket.status) $($bracket.body)"

$kickBlocked = Call $mod.session 'DELETE' "/teams/$teamAId/members/$($p6.id)"
Check 'bloquear expulsión con torneo en curso' ($kickBlocked.status -eq 400 -and $kickBlocked.body -match 'torneo en curso') "$($kickBlocked.status) $($kickBlocked.body)"

$roleBlocked = Call $mod.session 'PUT' "/teams/$teamAId/members/$($p2.id)" @{ role = 'Member'; isStarter = $false }
Check 'bloquear cambio de rol con torneo en curso' ($roleBlocked.status -eq 400 -and $roleBlocked.body -match 'torneo en curso') "$($roleBlocked.status) $($roleBlocked.body)"

$leaveBlocked = Call $p3.session 'POST' "/teams/$teamAId/leave" @{}
Check 'bloquear salida del equipo con torneo en curso' ($leaveBlocked.status -eq 400 -and $leaveBlocked.body -match 'torneo en curso') "$($leaveBlocked.status) $($leaveBlocked.body)"

$deleteBlocked = Call $mod.session 'DELETE' "/teams/$teamAId"
Check 'bloquear eliminación del equipo con torneo en curso' ($deleteBlocked.status -eq 400 -and $deleteBlocked.body -match 'torneo en curso') "$($deleteBlocked.status) $($deleteBlocked.body)"

# ---------- finalizar y verificar snapshot de plantilla ----------
$matches = Json (Call $mod.session 'GET' "/tournaments/$tidMain/matches")
$final = @($matches | Where-Object { $_.playerA -and $_.playerB })[0]
Check 'llave por equipos generada' ($null -ne $final) ($matches | ConvertTo-Json -Depth 4 -Compress)

if ($null -ne $final) {
  $teamAIsA = ($final.playerA.teamId -eq $teamAId)
  $winBody = if ($teamAIsA) { @{ scoreA = 13; scoreB = 5 } } else { @{ scoreA = 5; scoreB = 13 } }
  $loseBody = if ($teamAIsA) { @{ scoreA = 5; scoreB = 13 } } else { @{ scoreA = 13; scoreB = 5 } }

  $resWin = Call $mod.session 'PUT' "/tournaments/$tidMain/matches/$($final.id)/result" $winBody
  $advWin = Call $mod.session 'POST' "/tournaments/$tidMain/matches/$($final.id)/advance" @{}
  $tFinished = Json (Call $mod.session 'GET' "/tournaments/$tidMain")
  Check 'finalizar torneo por equipos' ($resWin.status -eq 200 -and $advWin.status -eq 200 -and $tFinished.status -eq 'Finalizado') "$($resWin.status)/$($advWin.status) $($tFinished.status)"

  $p6AfterWin = PointsOf $mod.session $p6.id
  Check 'suplente inscripto suma como campeón (135)' ($p6AfterWin -eq 135) "$p6AfterWin"

  $kick = Call $mod.session 'DELETE' "/teams/$teamAId/members/$($p6.id)"
  Check 'expulsar integrante tras finalizar permitido' ($kick.status -eq 200) "$($kick.status) $($kick.body)"

  $fix = Call $mod.session 'PUT' "/tournaments/$tidMain/matches/$($final.id)/result" $loseBody
  Check 'corregir final y recalcular ranking' ($fix.status -eq 200) "$($fix.status) $($fix.body)"

  $p6AfterFix = PointsOf $mod.session $p6.id
  $p2AfterFix = PointsOf $mod.session $p2.id
  $q2AfterFix = PointsOf $mod.session $q2.id
  Check 'expulsado conserva puntos por plantilla congelada (70)' ($p6AfterFix -eq 70) "$p6AfterFix"
  Check 'compañero restante suma subcampeón (70)' ($p2AfterFix -eq 70) "$p2AfterFix"
  Check 'equipo B ahora campeón suma 135' ($q2AfterFix -eq 135) "$q2AfterFix"
}

# ---------- limpieza ----------
$toDelete = @(@($tidMain, $notOpen.id, $okDate.id, $ea.id, $eaDef.id, $rocket.id, $cs.id, $lol.id) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
$delOk = 0
foreach ($tid in $toDelete) {
  $r = Call $mod.session 'DELETE' "/tournaments/$tid"
  if ($r.status -eq 200) { $delOk++ }
}
Check 'limpieza de torneos' ($delOk -eq $toDelete.Count) "eliminados=$delOk de $($toDelete.Count)"

$delA = Call $mod.session 'DELETE' "/teams/$teamAId"
$delB = Call $mod.session 'DELETE' "/teams/$teamBId"
Check 'limpieza de equipos' ($delA.status -eq 200 -and $delB.status -eq 200) "$($delA.status)/$($delB.status)"

foreach ($u in $all) { & dotnet run --project $tool -- delete-user $u.username | Out-Null }
Check 'limpieza de usuarios' ($true) ''

Write-Host ""
Write-Host "PASS=$($script:pass) FAIL=$($script:fail)" -ForegroundColor Cyan
if ($script:fail -gt 0) { exit 1 }
