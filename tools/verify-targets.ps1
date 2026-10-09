# Offline check of every Harmony target this mod patches, against the installed Assembly-CSharp.dll (Mono.Cecil,
# nothing is executed). Run after an SPT/EFT update:  pwsh tools\verify-targets.ps1 -Game "<SPT folder>"
# For each target: the method exists exactly once, returns what the patch's __result expects, and has the
# parameters the patch injects by name. Exit code 1 if anything is off.
param([string]$Game = "G:\G Games\SPT4.1\SPT4.1 GAME")

Add-Type -Path (Join-Path $Game "BepInEx\core\Mono.Cecil.dll")
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $Game "EscapeFromTarkov_Data\Managed\Assembly-CSharp.dll"))

function Find-Type([string]$name) {
    foreach ($t in $asm.MainModule.GetTypes()) { if ($t.FullName -eq $name) { return $t } }
    return $null
}

# Type, Method, ParamTypes (null = any, must then be unique), Return, injected parameter names
$targets = @(
    @{ T = "EFT.HideoutPlayerOwner"; M = "Init"; R = "System.Void"; P = @() },
    @{ T = "EFT.HideoutPlayerOwner"; M = "TranslateCommand"; R = "EFT.InputSystem.InputNode/ETranslateResult"; P = @("command") },
    @{ T = "EFT.HideoutPlayerOwner"; M = "TranslateInventoryScreenInput"; R = "System.Boolean"; P = @("command") },
    @{ T = "EFT.HideoutPlayerOwner"; M = "TranslateExitScreenInput"; R = "System.Boolean"; P = @("command") },
    @{ T = "EFT.HideoutPlayerOwner"; M = "EnterShootingRange"; R = "System.Void"; P = @() },
    @{ T = "EFT.HideoutPlayerOwner"; M = "ExitShootingRange"; R = "System.Threading.Tasks.Task"; P = @() },
    @{ T = "EFT.HideoutPlayerOwner"; M = "DecidePatrolStatus"; R = "System.Void"; P = @() },
    @{ T = "EFT.HideoutPlayerOwner"; M = "FlashlightAvailable"; R = "System.Boolean"; P = @() },
    @{ T = "EFT.HideoutPlayerOwner"; M = "SetShootingRangeStatus"; R = "System.Threading.Tasks.Task"; P = @("status") },
    @{ T = "EFT.InteractionContextHelper"; M = "GetAvailableActions"; Sig = @("EFT.HideoutPlayerOwner", "EFT.Hideout.HideoutArea"); R = "EFT.UI.AvailableInteractionState"; P = @("owner") },
    @{ T = "EFT.Hideout.WorkoutBehaviour"; M = "StartQte"; R = "System.Void"; P = @("owner", "qteData") },
    @{ T = "EFT.UI.EftBattleUIScreen/HideoutBattleUIScreenController"; M = "get_AllowHealthPanel"; R = "System.Boolean"; P = @() },
    @{ T = "EFT.HideoutGrenadeInputTranslator"; M = "TranslateCommand"; R = "EFT.InputSystem.InputNode/ETranslateResult"; P = @("command") },
    @{ T = "EFT.HideoutPlayerInputTranslator"; M = "vmethod_1"; R = "System.Void"; P = @() },
    @{ T = "EFT.PlayerInputTranslator"; M = "vmethod_1"; R = "System.Void"; P = @() },
    @{ T = "EFT.HideoutPlayer"; M = "InitVaultingComponent"; R = "System.Void"; P = @("aiControlled") },
    @{ T = "EFT.Player"; M = "InitVaultingComponent"; R = "System.Void"; P = @("aiControlled") },
    @{ T = "EFT.Player"; M = "ApplyDamageInfo"; R = "System.Void"; P = @() },
    @{ T = "EFT.Player"; M = "SetItemInHands"; R = "System.Void"; P = @("item") },
    @{ T = "EFT.UI.EftBattleUIScreen/HideoutBattleUIScreenController"; M = "OnShootingRangeStatus"; R = "System.Void"; P = @("status") },
    @{ T = "EFT.HideoutPlayer"; M = "ExecuteSkill"; Sig = @("System.Action"); R = "System.Void"; P = @("action") },
    @{ T = "EFT.HideoutPlayer"; M = "ExecuteShotSkill"; R = "System.Void"; P = @("weapon") },
    @{ T = "EFT.Player"; M = "ExecuteShotSkill"; R = "System.Void"; P = @("weapon") },
    @{ T = "EFT.UI.ItemUiContext"; M = "ThrowItem"; R = "System.Threading.Tasks.Task"; P = @("item") },
    @{ T = "RainCondensatorHelper"; M = "SetEnabled"; R = "System.Void"; P = @("rainCondensators", "enabled") },
    @{ T = "EFT.UI.AmmoSelector"; M = "SetSelectedState"; R = "System.Void"; P = @("index", "isSelected") },
    @{ T = "EFT.Player/FirearmController/Idling"; M = "SetTriggerPressed"; R = "System.Void"; P = @("pressed") },
    @{ T = "EFT.Player/FirearmController/RocketLauncherFire"; M = "OnFireEvent"; R = "System.Void"; P = @() }
)

$bad = 0
foreach ($x in $targets) {
    $type = Find-Type $x.T
    if (-not $type) { Write-Host "MISSING TYPE   $($x.T)" -ForegroundColor Red; $bad++; continue }
    $methods = @($type.Methods | Where-Object { $_.Name -eq $x.M })
    if ($x.Sig) {
        $methods = @($methods | Where-Object { (($_.Parameters | ForEach-Object { $_.ParameterType.FullName }) -join ",") -eq ($x.Sig -join ",") })
    }
    if ($methods.Count -ne 1) { Write-Host "FOUND $($methods.Count)x   $($x.T)::$($x.M)" -ForegroundColor Red; $bad++; continue }
    $m = $methods[0]
    $problems = @()
    if (-not $m.HasBody) { $problems += "no body" }
    if ($m.ReturnType.FullName -ne $x.R) { $problems += "returns $($m.ReturnType.FullName), expected $($x.R)" }
    foreach ($p in $x.P) { if (-not ($m.Parameters | Where-Object { $_.Name -eq $p })) { $problems += "no parameter '$p'" } }
    if ($problems.Count) { Write-Host "MISMATCH       $($x.T)::$($x.M): $($problems -join '; ')" -ForegroundColor Red; $bad++ }
    else { Write-Host "ok             $($x.T)::$($x.M)($(($m.Parameters | ForEach-Object { $_.ParameterType.Name + ' ' + $_.Name }) -join ', '))" }
}

# members read by name or relied on for behaviour
$owner = Find-Type "EFT.GamePlayerOwner"
$field = $owner.Fields | Where-Object { $_.Name -eq "BattleUIScreenController" }
if ($field -and $field.FieldType.FullName -eq "EFT.UI.IBattleUIScreenController") { Write-Host "ok             EFT.GamePlayerOwner.BattleUIScreenController : $($field.FieldType.Name)" }
else { Write-Host "MISMATCH       EFT.GamePlayerOwner.BattleUIScreenController ($($field.FieldType.FullName))" -ForegroundColor Red; $bad++ }

$ctx = Find-Type "EFT.UI.ItemUiContext"
$field = $ctx.Fields | Where-Object { $_.Name -eq "_itemController" }
if ($field -and $field.FieldType.FullName -eq "EFT.InventoryLogic.ItemController") { Write-Host "ok             EFT.UI.ItemUiContext._itemController : $($field.FieldType.Name)" }
else { Write-Host "MISMATCH       EFT.UI.ItemUiContext._itemController ($($field.FieldType.FullName))" -ForegroundColor Red; $bad++ }

foreach ($check in @(@("EFT.Player", "_handsController"), @("EFT.UI.AmmoSelector", "_magazinesViews"))) {
    $f = (Find-Type $check[0]).Fields | Where-Object { $_.Name -eq $check[1] }
    if ($f) { Write-Host "ok             $($check[0]).$($check[1]) : $($f.FieldType.Name)" }
    else { Write-Host "MISSING FIELD  $($check[0]).$($check[1])" -ForegroundColor Red; $bad++ }
}

if ($bad) { Write-Host "`n$bad problem(s)" -ForegroundColor Red; exit 1 }
Write-Host "`nAll targets resolve."
