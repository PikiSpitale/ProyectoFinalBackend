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

function Register-SmokeUser([string]$suffix) {
  $user = "rk${stamp}$suffix"
  $sess = New-ApiSession
  $r = Call $sess 'POST' '/auth/register' @{
    username = $user; email = "$user@example.com"; password = $pw; confirmPassword = $pw
    games = @(@{ gameName = 'Valorant'; inGameName = "$user#EUW"; rank = 'Oro 1' })
  }
  return [pscustomobject]@{ username = $user; session = $sess; register = $r; id = (Json $r).user.id }
}

$script:reader = $null
function PointsOf([string]$uid, [string]$game = 'Valorant') {
  $rows = Json (Call $script:reader 'GET' "/rankings?gameName=$([uri]::EscapeDataString($game))")
  $row = @($rows | Where-Object { $_.userId -eq $uid })[0]
  if ($null -eq $row) { return -1 }
  return [int]$row.points
}

function RowOf([string]$uid) {
  $rows = Json (Call $script:reader 'GET' '/rankings?gameName=Valorant')
  return @($rows | Where-Object { $_.userId -eq $uid })[0]
}

function ProgressOf([string]$uid) {
  return Json (Call $script:reader 'GET' "/rankings/users/$uid")
}

Write-Host "== Smoke ranking web + RF-14 (stamp $stamp) ==" -ForegroundColor Cyan

# ---------- usuarios ----------
$users = @()
foreach ($s in @('a','b','c','d','e','f','g','h','i','j','k','l','m','n')) { $users += Register-SmokeUser $s }
$badReg = @($users | Where-Object { $_.register.status -ne 201 })
Check 'registro de 14 usuarios' ($badReg.Count -eq 0) (($badReg | ForEach-Object { "$($_.username)=$($_.register.status) $($_.register.body)" }) -join ' | ')

$u1 = $users[0]; $u2 = $users[1]; $u3 = $users[2]; $u4 = $users[3]
$u5 = $users[4]; $u6 = $users[5]; $u7 = $users[6]; $u8 = $users[7]
$u9 = $users[8]; $u10 = $users[9]; $u11 = $users[10]; $u12 = $users[11]
$u13 = $users[12]; $u14 = $users[13]
$script:reader = $u1.session

# Ranking arranca en 0
$p1 = ProgressOf $u1.id
$vg1 = @($p1.games | Where-Object { $_.gameName -eq 'Valorant' })[0]
Check 'ranking arranca en 0 puntos' ($p1.totalPoints -eq 0 -and $vg1.points -eq 0 -and @($vg1.history).Count -eq 0) ($p1 | ConvertTo-Json -Depth 4 -Compress)

# ---------- moderador ----------
& dotnet run --project $tool -- promote $u1.username | Out-Null
$login = Call $u1.session 'POST' '/auth/login' @{ email = "$($u1.username)@example.com"; password = $pw }
Check 'login moderador' ($login.status -eq 200 -and (Json $login).user.role -eq 'Moderator') $login.body

# ---------- torneo individual ----------
# La inscripción abre 30 minutos antes del inicio; se crea con 31 para poder esperar la apertura.
$start = (Get-Date).ToUniversalTime().AddMinutes(31)
$title = "Rank Cup $stamp"
$create = Call $u1.session 'POST' '/tournaments' @{
  title = $title; gameName = 'Valorant'; bannerUrl = ''; prizePool = 0; maxParticipants = 8
  rules = 'Ranking smoke'; rankConditions = 'Sin restriccion'
  startDate = $start.ToString('o'); endDate = $start.AddHours(1).ToString('o'); tournamentType = 'Individual'
}
$list = Json (Call $u1.session 'GET' '/tournaments')
$t = @($list | Where-Object { $_.title -eq $title })[0]
$tid = $t.id
Check 'crear torneo individual' ($create.status -eq 200 -and -not [string]::IsNullOrWhiteSpace($tid)) "$($create.status) $($create.body)"

# La inscripción se habilita 30 minutos antes: esperamos a que abra la ventana.
Write-Host "Esperando la apertura de la inscripción (65 s)..." -ForegroundColor Yellow
Start-Sleep -Seconds 65

$regs = @()
foreach ($u in @($u1,$u2,$u3,$u4)) {
  $r = Call $u.session 'POST' "/tournaments/$tid/register" @{ userId = $u.id; username = $u.username }
  $regs += "$($u.username)=$($r.status)"
}
Check 'inscribir 4 jugadores' (@($regs | Where-Object { $_ -notmatch '=200$' }).Count -eq 0) ($regs -join ', ')

$bracket = Call $u1.session 'POST' "/tournaments/$tid/bracket"
Check 'generar llaves' ($bracket.status -eq 200) "$($bracket.status) $($bracket.body)"

$sums = @()
foreach ($u in @($u1,$u2,$u3,$u4)) { $sums += "$($u.username)=$(PointsOf $u.id)" }
Check 'participacion +10 desde En curso' (@($sums | Where-Object { $_ -notmatch '=10$' }).Count -eq 0) ($sums -join ', ')

$matches = Json (Call $u1.session 'GET' "/tournaments/$tid/matches")
$rounds = @($matches | ForEach-Object { $_.round } | Sort-Object -Unique)
$firstRound = ($rounds | Measure-Object -Minimum).Minimum
$semis = @($matches | Where-Object { $_.round -eq $firstRound -and $_.playerA -and $_.playerB })
Check 'dos semifinales' ($semis.Count -eq 2) ($semis | ConvertTo-Json -Depth 4 -Compress)

$semiLosers = @()
foreach ($m in $semis) {
  $res = Call $u1.session 'PUT' "/tournaments/$tid/matches/$($m.id)/result" @{ scoreA = 13; scoreB = 5 }
  Check "resultado semifinal $($m.position)" ($res.status -eq 200) $res.body
  $semiLosers += $m.playerB.userId
}
Check 'semifinalistas +35 y eliminados +20' ((PointsOf $semis[0].playerA.userId) -eq 35 -and (PointsOf $semiLosers[0]) -eq 20) "$(PointsOf $semis[0].playerA.userId) / $(PointsOf $semiLosers[0])"

foreach ($m in $semis) {
  $adv = Call $u1.session 'POST' "/tournaments/$tid/matches/$($m.id)/advance" @{}
  Check "avanzar semifinal $($m.position)" ($adv.status -eq 200) "$($adv.status) $($adv.body)"
}

$afterAdvance = Json (Call $u1.session 'GET' "/tournaments/$tid/matches")
$final = @($afterAdvance | Where-Object { $_.round -ne $firstRound -and $_.playerA -and $_.playerB })[0]
Check 'final definida' ($null -ne $final) ($afterAdvance | ConvertTo-Json -Depth 4 -Compress)

$champion = $final.playerA.userId
$runnerUp = $final.playerB.userId
$finalRes = Call $u1.session 'PUT' "/tournaments/$tid/matches/$($final.id)/result" @{ scoreA = 13; scoreB = 5 }
Check 'resultado final' ($finalRes.status -eq 200) $finalRes.body
$advFinal = Call $u1.session 'POST' "/tournaments/$tid/matches/$($final.id)/advance" @{}
Check 'avanzar final' ($advFinal.status -eq 200) "$($advFinal.status) $($advFinal.body)"

$tAfter = Json (Call $u1.session 'GET' "/tournaments/$tid")
Check 'torneo finalizado' ($tAfter.status -eq 'Finalizado') $tAfter.status

$champPts = PointsOf $champion
$runnerPts = PointsOf $runnerUp
$semiPts = @($semiLosers | ForEach-Object { PointsOf $_ })
Check 'campeon 160 / subcampeon 95' ($champPts -eq 160 -and $runnerPts -eq 95) "$champPts / $runnerPts"
Check 'semifinalistas 45 c/u' (@($semiPts | Where-Object { $_ -ne 45 }).Count -eq 0) ($semiPts -join ', ')

$champRow = RowOf $champion
$runnerRow = RowOf $runnerUp
$semiRows = @($semiLosers | ForEach-Object { RowOf $_ })
Check 'posiciones relativas campeon > subcampeon > semis' ($champRow.position -lt $runnerRow.position -and @($semiRows | Where-Object { $_.position -le $runnerRow.position }).Count -eq 0) "champ=$($champRow.position) runner=$($runnerRow.position) semis=$(($semiRows | ForEach-Object { $_.position }) -join ',')"
Check 'campeon con torneo ganado' ($champRow.tournamentsWon -eq 1 -and $runnerRow.tournamentsWon -eq 0) "$($champRow.tournamentsWon) / $($runnerRow.tournamentsWon)"

$champProg = ProgressOf $champion
$champGame = @($champProg.games | Where-Object { $_.gameName -eq 'Valorant' })[0]
$histSum = ($champGame.history | Measure-Object -Property points -Sum).Sum
Check 'historial por torneo del campeon' ($histSum -eq 160 -and @($champGame.history | Where-Object { $_.type -eq 'Champion' }).Count -eq 1 -and $champGame.tournamentsPlayed -eq 1) "sum=$histSum type=$($champGame.history.type -join ',')"

# ---------- correccion de resultado final (idempotencia) ----------
$fix = Call $u1.session 'PUT' "/tournaments/$tid/matches/$($final.id)/result" @{ scoreA = 5; scoreB = 13 }
Check 'corregir resultado final' ($fix.status -eq 200) $fix.body
$newChamp = $runnerUp
$oldChamp = $champion
Check 'puntos se recalculan sin duplicar' ((PointsOf $newChamp) -eq 160 -and (PointsOf $oldChamp) -eq 95) "$(PointsOf $newChamp) / $(PointsOf $oldChamp)"
$newChampProg = ProgressOf $newChamp
$newChampGame = @($newChampProg.games | Where-Object { $_.gameName -eq 'Valorant' })[0]
$newChampTypes = @($newChampGame.history | ForEach-Object { $_.type })
Check 'historial del nuevo campeon sin Champion duplicado' (@($newChampTypes | Where-Object { $_ -eq 'Champion' }).Count -eq 1 -and (($newChampGame.history | Measure-Object -Property points -Sum).Sum -eq 160)) ($newChampTypes -join ', ')

# ---------- filtro por juego (RF-34) ----------
$lolPlayers = Json (Call $u1.session 'GET' '/rankings?gameName=League%20of%20Legends')
$ids = @($users | ForEach-Object { $_.id })
Check 'RF-34 filtro de jugadores por juego' (@($lolPlayers | Where-Object { $ids -contains $_.userId }).Count -eq 0) ($lolPlayers | ConvertTo-Json -Depth 3 -Compress)

# ---------- juego nuevo arranca en 0 y desvincular borra historial ----------
$addCs = Call $u4.session 'POST' '/profile/games' @{ gameName = 'Counter-Strike 2'; inGameName = 'rkcssmoke'; rank = 'Premier 12000' }
$p4 = ProgressOf $u4.id
$cs = @($p4.games | Where-Object { $_.gameName -eq 'Counter-Strike 2' })[0]
Check 'juego nuevo arranca en 0' ($addCs.status -eq 200 -and $cs.points -eq 0 -and @($cs.history).Count -eq 0) ($p4 | ConvertTo-Json -Depth 4 -Compress)
$delCs = Call $u4.session 'DELETE' '/profile/games/Counter-Strike%202'
$p4b = ProgressOf $u4.id
Check 'eliminar juego quita el juego del ranking' ($delCs.status -eq 200 -and @($p4b.games | Where-Object { $_.gameName -eq 'Counter-Strike 2' }).Count -eq 0) ($p4b | ConvertTo-Json -Depth 4 -Compress)

# ---------- equipos: RF-09/11 + ranking por equipo ----------
$teamAName = "Rank Team A $stamp"
$teamBName = "Rank Team B $stamp"
$teamA = Call $u1.session 'POST' '/teams' @{ name = $teamAName; tag = "TA$($stamp % 1000)"; logoUrl = ''; gameName = 'Valorant'; joinPolicy = 'InviteOnly' }
$teamB = Call $u8.session 'POST' '/teams' @{ name = $teamBName; tag = "TB$($stamp % 1000)"; logoUrl = ''; gameName = 'Valorant'; joinPolicy = 'InviteOnly' }
$teamAId = (Json $teamA).id
$teamBId = (Json $teamB).id
Check 'crear dos equipos' ($teamA.status -eq 201 -and $teamB.status -eq 201 -and $teamAId -and $teamBId) "$($teamA.status)/$($teamB.status) $($teamA.body) $($teamB.body)"

$teamAMembers = @($u1,$u2,$u3,$u4,$u5,$u6,$u7)
$teamBMembers = @($u8,$u9,$u10,$u11,$u12,$u13,$u14)
$invites = @()
foreach ($member in @($u2,$u3,$u4,$u5,$u6,$u7)) {
  $inv = Call $u1.session 'POST' "/teams/$teamAId/invitations" @{ username = $member.username }
  $invId = (Json $inv).id
  $acc = Call $member.session 'POST' "/teams/invitations/$invId/respond" @{ response = 'Accept' }
  $invites += "$($member.username)=$($inv.status)/$($acc.status)"
}
foreach ($member in @($u9,$u10,$u11,$u12,$u13,$u14)) {
  $inv = Call $u8.session 'POST' "/teams/$teamBId/invitations" @{ username = $member.username }
  $invId = (Json $inv).id
  $acc = Call $member.session 'POST' "/teams/invitations/$invId/respond" @{ response = 'Accept' }
  $invites += "$($member.username)=$($inv.status)/$($acc.status)"
}
Check 'invitar y aceptar integrantes' (@($invites | Where-Object { $_ -notmatch '=200/200$' }).Count -eq 0) ($invites -join ', ')

# RF-15: exactamente 5 titulares y 2 suplentes por equipo
$subs = @()
foreach ($pair in @(@($u1,$u6), @($u1,$u7), @($u8,$u13), @($u8,$u14))) {
  $captain = $pair[0]; $member = $pair[1]
  $teamId = if ($captain.id -eq $u1.id) { $teamAId } else { $teamBId }
  $r = Call $captain.session 'PUT' "/teams/$teamId/members/$($member.id)" @{ role = 'Member'; isStarter = $false }
  $subs += "$($member.username)=$($r.status)"
}
Check 'marcar 2 suplentes por equipo' (@($subs | Where-Object { $_ -notmatch '=200$' }).Count -eq 0) ($subs -join ', ')

# Se reutiliza la misma fecha de inicio: la inscripción ya está abierta.
$start2 = $start
$title2 = "Rank Team Cup $stamp"
$create2 = Call $u1.session 'POST' '/tournaments' @{
  title = $title2; gameName = 'Valorant'; bannerUrl = ''; prizePool = 0; maxParticipants = 8
  rules = 'Ranking equipos'; rankConditions = 'Sin restriccion'
  startDate = $start2.ToString('o'); endDate = $start2.AddHours(1).ToString('o'); tournamentType = 'Equipo'
}
$list2 = Json (Call $u1.session 'GET' '/tournaments')
$t2 = @($list2 | Where-Object { $_.title -eq $title2 })[0]
$tid2 = $t2.id
Check 'crear torneo por equipos' ($create2.status -eq 200 -and $null -ne $tid2) "$($create2.status) $($create2.body)"

$regA = Call $u1.session 'POST' "/tournaments/$tid2/register-team" @{ teamId = $teamAId }
$regB = Call $u8.session 'POST' "/tournaments/$tid2/register-team" @{ teamId = $teamBId }
Check 'inscribir dos equipos' ($regA.status -eq 200 -and $regB.status -eq 200) "$($regA.status)/$($regB.status) $($regA.body) $($regB.body)"

$beforeTeamA = @{}
foreach ($u in $teamAMembers) { $beforeTeamA[$u.id] = PointsOf $u.id }
$beforeTeamB = @{}
foreach ($u in $teamBMembers) { $beforeTeamB[$u.id] = PointsOf $u.id }

$bracket2 = Call $u1.session 'POST' "/tournaments/$tid2/bracket"
Check 'generar llaves por equipo' ($bracket2.status -eq 200) "$($bracket2.status) $($bracket2.body)"

$teamRowsAfterBracket = Json (Call $u1.session 'GET' '/rankings/teams?gameName=Valorant')
$rowAB = @($teamRowsAfterBracket | Where-Object { $_.teamId -eq $teamAId })[0]
$rowBB = @($teamRowsAfterBracket | Where-Object { $_.teamId -eq $teamBId })[0]
Check 'participacion de equipo +10' ($rowAB.points -eq 10 -and $rowBB.points -eq 10) "$($rowAB.points) / $($rowBB.points)"

$matches2 = Json (Call $u1.session 'GET' "/tournaments/$tid2/matches")
$final2 = @($matches2 | Where-Object { $_.playerA -and $_.playerB })[0]
Check 'final por equipos definida' ($null -ne $final2) ($matches2 | ConvertTo-Json -Depth 5 -Compress)

$winnerTeamId = $final2.playerA.teamId
$loserTeamId = $final2.playerB.teamId
$res2 = Call $u1.session 'PUT' "/tournaments/$tid2/matches/$($final2.id)/result" @{ scoreA = 13; scoreB = 5 }
Check 'resultado final por equipos' ($res2.status -eq 200) $res2.body
$adv2 = Call $u1.session 'POST' "/tournaments/$tid2/matches/$($final2.id)/advance" @{}
Check 'avanzar final por equipos' ($adv2.status -eq 200) "$($adv2.status) $($adv2.body)"
$t2After = Json (Call $u1.session 'GET' "/tournaments/$tid2")
Check 'torneo por equipos finalizado' ($t2After.status -eq 'Finalizado') $t2After.status

$teamRows = Json (Call $u1.session 'GET' '/rankings/teams?gameName=Valorant')
$rowA = @($teamRows | Where-Object { $_.teamId -eq $winnerTeamId })[0]
$rowB = @($teamRows | Where-Object { $_.teamId -eq $loserTeamId })[0]
Check 'puntos de equipo: campeon 135 / subcampeon 70' ($rowA.points -eq 135 -and $rowB.points -eq 70) "$($rowA.points) / $($rowB.points)"
Check 'ranking de equipos ordenado' ($rowA.position -lt $rowB.position) "$($rowA.position) / $($rowB.position)"

$lolTeams = Json (Call $u1.session 'GET' '/rankings/teams?gameName=League%20of%20Legends')
Check 'RF-34 filtro de equipos por juego' (@($lolTeams | Where-Object { $_.teamId -eq $teamAId -or $_.teamId -eq $teamBId }).Count -eq 0) ($lolTeams | ConvertTo-Json -Depth 3 -Compress)

# El equipo A (u1..u7) gano y el B (u8..u14) perdio la final.
$expectedA = if ($winnerTeamId -eq $teamAId) { $teamAMembers } else { $teamBMembers }
$expectedB = if ($winnerTeamId -eq $teamAId) { $teamBMembers } else { $teamAMembers }
$beforeMapA = if ($winnerTeamId -eq $teamAId) { $beforeTeamA } else { $beforeTeamB }
$beforeMapB = if ($winnerTeamId -eq $teamAId) { $beforeTeamB } else { $beforeTeamA }

$deltasA = @()
foreach ($u in $expectedA) { $deltasA += "$($u.username)=$((PointsOf $u.id) - $beforeMapA[$u.id])" }
Check 'miembros del campeon suman +135' (@($deltasA | Where-Object { $_ -notmatch '=135$' }).Count -eq 0) ($deltasA -join ', ')

$deltasB = @()
foreach ($u in $expectedB) { $deltasB += "$($u.username)=$((PointsOf $u.id) - $beforeMapB[$u.id])" }
Check 'miembros del subcampeon suman +70' (@($deltasB | Where-Object { $_ -notmatch '=70$' }).Count -eq 0) ($deltasB -join ', ')

# ---------- RF-14 roles y capitania ----------
$toU2 = Call $u1.session 'PUT' "/teams/$teamAId/members/$($u2.id)" @{ role = 'Captain'; isStarter = $true }
$teamAfterRole = Json (Call $u1.session 'GET' "/teams/$teamAId")
$u1Role = @($teamAfterRole.members | Where-Object { $_.userId -eq $u1.id })[0].role
$u2Role = @($teamAfterRole.members | Where-Object { $_.userId -eq $u2.id })[0].role
Check 'RF-14 transferir capitania' ($toU2.status -eq 200 -and $teamAfterRole.captainId -eq $u2.id -and $u2Role -eq 'Captain' -and $u1Role -eq 'Member') "captain=$($teamAfterRole.captainId) u2=$u2Role u1=$u1Role"

$selfDemote = Call $u2.session 'PUT' "/teams/$teamAId/members/$($u2.id)" @{ role = 'Member'; isStarter = $true }
Check 'capitan no puede autodegradarse' ($selfDemote.status -eq 400 -and $selfDemote.body -match 'capit') "$($selfDemote.status) $($selfDemote.body)"

$notCaptain = Call $u3.session 'PUT' "/teams/$teamAId/members/$($u3.id)" @{ role = 'Captain'; isStarter = $true }
Check 'integrante no puede cambiar roles' ($notCaptain.status -eq 400 -and $notCaptain.body -match 'capit') "$($notCaptain.status) $($notCaptain.body)"

$backToU1 = Call $u2.session 'PUT' "/teams/$teamAId/members/$($u1.id)" @{ role = 'Captain'; isStarter = $true }
$teamBack = Json (Call $u1.session 'GET' "/teams/$teamAId")
Check 'devolver capitania' ($backToU1.status -eq 200 -and $teamBack.captainId -eq $u1.id) "captain=$($teamBack.captainId)"

# ---------- desvincular juego borra historial de ranking ----------
$u14Expected = if ($winnerTeamId -eq $teamBId) { 135 } else { 70 }
$u14Before = ProgressOf $u14.id
$delGame = Call $u14.session 'DELETE' '/profile/games/Valorant'
$u14After = ProgressOf $u14.id
Check 'RF-08 desvincular borra puntos del ranking' ($delGame.status -eq 200 -and $u14Before.totalPoints -eq $u14Expected -and $u14After.totalPoints -eq 0 -and @($u14After.games).Count -eq 0) "antes=$($u14Before.totalPoints) esperado=$u14Expected despues=$($u14After.totalPoints) games=$(@($u14After.games).Count)"

# ---------- limpieza ----------
$delT1 = Call $u1.session 'DELETE' "/tournaments/$tid"
$delT2 = Call $u1.session 'DELETE' "/tournaments/$tid2"
$delA = Call $u1.session 'DELETE' "/teams/$teamAId"
$delB = Call $u1.session 'DELETE' "/teams/$teamBId"
Check 'limpieza de torneos y equipos' ($delT1.status -eq 200 -and $delT2.status -eq 200 -and $delA.status -eq 200 -and $delB.status -eq 200) "$($delT1.status)/$($delT2.status)/$($delA.status)/$($delB.status)"

foreach ($u in $users) { & dotnet run --project $tool -- delete-user $u.username | Out-Null }
$finalRows = Json (Call $u1.session 'GET' '/rankings?gameName=Valorant')
$left = @($finalRows | Where-Object { $ids -contains $_.userId })
Check 'limpieza de usuarios de prueba' ($left.Count -eq 0) ($left | ConvertTo-Json -Depth 3 -Compress)

Write-Host ""
Write-Host "PASS=$($script:pass) FAIL=$($script:fail)" -ForegroundColor Cyan
if ($script:fail -gt 0) { exit 1 }
