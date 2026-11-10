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
  $user = "smoke$stamp$suffix"
  $sess = New-ApiSession
  $r = Call $sess 'POST' '/auth/register' @{
    username = $user; email = "$user@example.com"; password = $pw; confirmPassword = $pw
    games = @(@{ gameName = 'Valorant'; inGameName = "$user#EUW"; rank = $rank })
  }
  return [pscustomobject]@{ username = $user; session = $sess; register = $r; id = (Json $r).user.id }
}

Write-Host "== Smoke test M1/M3/M4 (stamp $stamp) ==" -ForegroundColor Cyan

# ---------- M1: registro / sesión / perfil / juegos ----------
$u1 = Register-SmokeUser 'a' 'Platino 1'
Check 'RF-01 registro usuario 1 (201)' ($u1.register.status -eq 201) $u1.register.body

$me = Call $u1.session 'GET' '/auth/me'
Check 'RF-02 sesión /me con cookie' ($me.status -eq 200 -and (Json $me).user.username -eq $u1.username) $me.body

$upd = Call $u1.session 'PUT' '/profile/me' @{
  username = $u1.username; email = $u1.username + '@example.com'; avatarUrl = 'https://example.com/a.png'
}
Check 'RF-03 actualizar perfil' ($upd.status -eq 200 -and (Json $upd).avatarUrl -eq 'https://example.com/a.png') $upd.body

$addGame = Call $u1.session 'POST' '/profile/games' @{ gameName = 'Counter-Strike 2'; inGameName = 'smokecs'; rank = 'Premier 12000' }
Check 'RF-06 asociar segundo juego' ($addGame.status -eq 200 -and @((Json $addGame).games).Count -eq 2) $addGame.body

$updGame = Call $u1.session 'PUT' '/profile/games' @{ gameName = 'Valorant'; inGameName = 'smoke#EUW'; rank = 'Platino 1' }
Check 'RF-07 modificar rango' ($updGame.status -eq 200) $updGame.body

$delGame = Call $u1.session 'DELETE' '/profile/games/Counter-Strike%202'
Check 'RF-08 eliminar juego asociado' ($delGame.status -eq 200 -and @((Json $delGame).games).Count -eq 1) $delGame.body

# Email duplicado (RF-04)
$dupSess = New-ApiSession
$dup = Call $dupSess 'POST' '/auth/register' @{
  username = "smoke${stamp}dup"; email = "$($u1.username)@example.com"; password = $pw; confirmPassword = $pw
}
Check 'RF-04 email duplicado rechazado' ($dup.status -eq 400 -or $dup.status -eq 409) "$($dup.status) $($dup.body)"

# ---------- Promover usuario 1 a Moderador y refrescar sesión ----------
$tool = if ($env:SMOKE_DBTOOL) { $env:SMOKE_DBTOOL } else { Join-Path $PSScriptRoot '..\nexus-dbtool\nexus-dbtool.csproj' }
& dotnet run --project $tool -- promote $u1.username | Out-Null
$login = Call $u1.session 'POST' '/auth/login' @{ email = "$($u1.username)@example.com"; password = $pw }
Check 'RF-02 login moderador' ($login.status -eq 200 -and (Json $login).user.role -eq 'Moderator') $login.body
$u1TokenRole = (Json $login).user.role

# ---------- M3: torneos ----------
# La inscripción abre 30 minutos antes del inicio; se crea con 31 para poder esperar la apertura.
$start = (Get-Date).ToUniversalTime().AddMinutes(31)
$end = $start.AddMinutes(60)
$title = "Smoke Cup $stamp"
$createBody = @{
  title = $title; gameName = 'Valorant'; bannerUrl = ''; prizePool = 0; maxParticipants = 8
  rules = 'Reglas smoke'; rankConditions = 'Mínimo: Platino 1'
  startDate = $start.ToString('o'); endDate = $end.ToString('o'); tournamentType = 'Individual'
}
$create = Call $u1.session 'POST' '/tournaments' $createBody
Check 'RF-16 crear torneo' ($create.status -eq 200) $create.body

$list = Call $u1.session 'GET' '/tournaments'
$t = @((Json $list) | Where-Object { $_.title -eq $title })[0]
Check 'torneo aparece en listado' ($null -ne $t) (Json $list | ConvertTo-Json -Depth 3)
$tid = $t.id

# Condición estructurada inválida
$badBody = $createBody.Clone(); $badBody.title = "$title bad"; $badBody.rankConditions = 'Mínimo: RangoInventado'
$bad = Call $u1.session 'POST' '/tournaments' $badBody
Check 'RF-16 rechaza rango inexistente' ($bad.status -eq 400 -and $bad.body -match 'no es válida') "$($bad.status) $($bad.body)"

# RF-19 filtro por rango
$filterLow = Call $u1.session 'GET' '/tournaments?gameName=Valorant&rank=Oro%201'
$filterOk = Call $u1.session 'GET' '/tournaments?gameName=Valorant&rank=Platino%201'
$inLow = @((Json $filterLow) | Where-Object { $_.id -eq $tid }).Count -gt 0
$inOk = @((Json $filterOk) | Where-Object { $_.id -eq $tid }).Count -gt 0
Check 'RF-19 rango Oro 1 excluye torneo mínimo Platino 1' (-not $inLow) $filterLow.body
Check 'RF-19 rango Platino 1 incluye torneo' ($inOk) $filterOk.body

# RF-17 modificar torneo
$updT = Call $u1.session 'PUT' "/tournaments/$tid" @{ title = "$title upd"; maxParticipants = 8 }
Check 'RF-17 modificar torneo' ($updT.status -eq 200 -and (Json $updT).title -eq "$title upd") $updT.body
$changeGame = Call $u1.session 'PUT' "/tournaments/$tid" @{ gameName = 'League of Legends' }
# sin participantes todavía se permite el cambio; lo revertimos enseguida
Check 'RF-17 cambio de juego sin inscritos permitido' ($changeGame.status -eq 200) $changeGame.body
$revertGame = Call $u1.session 'PUT' "/tournaments/$tid" @{ gameName = 'Valorant' }
Check 'RF-17 restaurar juego' ($revertGame.status -eq 200) $revertGame.body

# La inscripción se habilita 30 minutos antes: esperamos a que abra la ventana.
Write-Host "Esperando la apertura de la inscripción (65 s)..." -ForegroundColor Yellow
Start-Sleep -Seconds 65

# RF-20 inscripción: rango insuficiente
$u5 = Register-SmokeUser 'e' 'Oro 2'
$regLow = Call $u5.session 'POST' "/tournaments/$tid/register" @{ userId = $u5.id; username = $u5.username }
Check 'RF-20 rechaza inscripción con rango insuficiente' ($regLow.status -eq 400 -and $regLow.body -match 'al menos Platino 1') "$($regLow.status) $($regLow.body)"

# RF-20 inscripción válida de 4 usuarios
$u2 = Register-SmokeUser 'b' 'Diamante 1'
$u3 = Register-SmokeUser 'c' 'Platino 2'
$u4 = Register-SmokeUser 'd' 'Inmortal 1'
$users = @($u1, $u2, $u3, $u4)
$regResults = @()
foreach ($u in $users) {
  $reg = Call $u.session 'POST' "/tournaments/$tid/register" @{ userId = $u.id; username = $u.username }
  $regResults += "$($u.username)=$($reg.status)"
}
Check 'RF-20 inscripción individual (4 usuarios)' (@($regResults | Where-Object { $_ -notmatch '=200' }).Count -eq 0) ($regResults -join ', ')

$regAgain = Call $u2.session 'POST' "/tournaments/$tid/register" @{ userId = $u2.id; username = $u2.username }
Check 'RF-20 no permite inscribirse dos veces' ($regAgain.status -eq 400 -and $regAgain.body -match 'Ya estás inscripto') $regAgain.body

# RF-22 verificar rango manualmente
$verify = Call $u1.session 'POST' "/tournaments/$tid/participants/$($u2.id)/verify-rank" @{ verified = $true }
$afterVerify = Json (Call $u1.session 'GET' "/tournaments/$tid")
$p2 = @($afterVerify.participants | Where-Object { $_.userId -eq $u2.id })[0]
Check 'RF-22 verificar rango' ($verify.status -eq 200 -and $p2.rankVerified -eq $true) "$($verify.status) $($verify.body)"

# RF-17 no cambiar juego con inscriptos
$badGame = Call $u1.session 'PUT' "/tournaments/$tid" @{ gameName = 'League of Legends' }
Check 'RF-17 rechaza cambiar juego con inscriptos' ($badGame.status -eq 400 -and $badGame.body -match 'inscriptos|inscritos') $badGame.body

# ---------- M4: bracket / resultados / avance / descalificación ----------
$bracket = Call $u1.session 'POST' "/tournaments/$tid/bracket"
$matches = Json $bracket
Check 'RF-23 generar llaves' ($bracket.status -eq 200 -and @($matches).Count -ge 3) "$($bracket.status) $($bracket.body)"

$rounds = @($matches | ForEach-Object { $_.round } | Sort-Object -Unique)
$firstRound = ($rounds | Measure-Object -Minimum).Minimum
$semis = @($matches | Where-Object { $_.round -eq $firstRound -and $_.playerA -and $_.playerB })

Check 'RF-24 consultar enfrentamientos' ($semis.Count -eq 2) ($semis | ConvertTo-Json -Depth 4)

if ($semis.Count -eq 2) {
  # RF-25 registrar resultados
  foreach ($m in $semis) {
    $res = Call $u1.session 'PUT' "/tournaments/$tid/matches/$($m.id)/result" @{ scoreA = 13; scoreB = 5 }
    Check "RF-25 resultado partido ronda $firstRound" ($res.status -eq 200) $res.body
  }

  # RF-26 corregir un resultado (invertir ganador)
  $editM = $semis[0]
  $edited = Call $u1.session 'PUT' "/tournaments/$tid/matches/$($editM.id)/result" @{ scoreA = 5; scoreB = 13 }
  Check 'RF-26 modificar resultado registrado' ($edited.status -eq 200) $edited.body

  # RF-27 ganador determinado
  $afterEdit = Json (Call $u1.session 'GET' "/tournaments/$tid/matches")
  $editedMatch = @($afterEdit | Where-Object { $_.id -eq $editM.id })[0]
  Check 'RF-27 ganador determinado por resultado' ($editedMatch.status -eq 'Finalizado' -and $editedMatch.winnerUserId -eq $editM.playerB.userId) ($editedMatch | ConvertTo-Json -Compress)

  # RF-28 avanzar ganadores manualmente
  foreach ($m in $semis) {
    $adv = Call $u1.session 'POST' "/tournaments/$tid/matches/$($m.id)/advance" @{}
    Check "RF-28 avanzar ganador partido $($m.position)" ($adv.status -eq 200) "$($adv.status) $($adv.body)"
  }

  $afterAdvance = Json (Call $u1.session 'GET' "/tournaments/$tid/matches")
  $final = @($afterAdvance | Where-Object { $_.round -ne $firstRound -and $_.playerA -and $_.playerB })[0]
  Check 'RF-28 final con ambos finalistas' ($null -ne $final) ($afterAdvance | ConvertTo-Json -Depth 4)

  if ($null -ne $final) {
    # RF-29 descalificar finalista => walkover y torneo finalizado
    $dq = Call $u1.session 'POST' "/tournaments/$tid/participants/$($final.playerB.userId)/disqualify" @{ reason = 'Smoke test' }
    $finalAfter = @((Json (Call $u1.session 'GET' "/tournaments/$tid/matches")) | Where-Object { $_.id -eq $final.id })[0]
    $tAfter = Json (Call $u1.session 'GET' "/tournaments/$tid")
    Check 'RF-29 descalificación con walkover' ($dq.status -eq 200 -and $finalAfter.isWalkover -eq $true) "$($dq.status) $($dq.body)"
    Check 'RF-30 torneo finalizado tras la final' ($tAfter.status -eq 'Finalizado') $tAfter.status
  }
}

# ---------- RF-18 cancelar torneo ----------
$cancelTitle = "Smoke Cancel $stamp"
$cStart = (Get-Date).ToUniversalTime().AddMinutes(31)
$cancelBody = @{
  title = $cancelTitle; gameName = 'Valorant'; bannerUrl = ''; prizePool = 0; maxParticipants = 8
  rules = 'Reglas'; rankConditions = 'Sin restricción'
  startDate = $cStart.ToString('o'); endDate = $cStart.AddHours(1).ToString('o'); tournamentType = 'Individual'
}
$cCreate = Call $u1.session 'POST' '/tournaments' $cancelBody
$cList = Json (Call $u1.session 'GET' '/tournaments')
$ct = @($cList | Where-Object { $_.title -eq $cancelTitle })[0]
$cancel = Call $u1.session 'POST' "/tournaments/$($ct.id)/cancel" @{}
$ctAfter = Json (Call $u1.session 'GET' "/tournaments/$($ct.id)")
Check 'RF-18 cancelar torneo' ($cCreate.status -eq 200 -and $cancel.status -eq 200 -and $ctAfter.status -eq 'Cancelado') "$($cancel.status) $($ctAfter.status)"

$cancelDel = Call $u1.session 'DELETE' "/tournaments/$($ct.id)"
Check 'limpieza: eliminar torneo cancelado' ($cancelDel.status -eq 200) $cancelDel.body

# ---------- Resultado ----------
Write-Host ""
Write-Host "PASS=$($script:pass) FAIL=$($script:fail)" -ForegroundColor Cyan
Write-Host "TEST_USERS=$($users.username -join ',')"
Write-Host "TEST_TOURNAMENT=$tid"
if ($script:fail -gt 0) { exit 1 }
