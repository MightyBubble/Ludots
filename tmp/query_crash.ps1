$events = Get-WinEvent -FilterHashtable @{LogName='Application'; ProviderName='Application Error'} -MaxEvents 8 -ErrorAction SilentlyContinue
foreach ($e in $events) {
    $head = ($e.Message -split "`r?`n")[0]
    $mod = ($e.Message -split "`r?`n") | Where-Object { $_ -match 'P\d+:|模块|Module' } | Select-Object -First 2
    Write-Output ("{0} | {1}" -f $e.TimeCreated.ToString('HH:mm:ss'), $head)
    if ($e.Message -match 'Raylib') { Write-Output ("  RAYLIB-CRASH: " + $e.Message.Substring(0, [Math]::Min(600, $e.Message.Length))) }
}
