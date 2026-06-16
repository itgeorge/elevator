$ports = Get-CimInstance Win32_SerialPort | Select-Object DeviceID, Name, Status
$ports | Format-Table -AutoSize | Out-String | Set-Content -Path "$PSScriptRoot\_com_ports.txt"

try {
    $p = New-Object System.IO.Ports.SerialPort 'COM4', 115200
    $p.Open()
    $p.Close()
    'COM4 open OK' | Set-Content -Path "$PSScriptRoot\_com_test.txt"
}
catch {
    $_.Exception.Message | Set-Content -Path "$PSScriptRoot\_com_test.txt"
}

Get-Process dotnet -ErrorAction SilentlyContinue |
    Select-Object Id, CPU, StartTime |
    Format-Table -AutoSize |
    Out-String |
    Set-Content -Path "$PSScriptRoot\_dotnet_procs.txt"
