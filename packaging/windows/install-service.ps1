param(
    [string]$PublishPath = "C:\Program Files\SqlAgent",
    [string]$ServiceName = "SqlAgent",
    [string]$DisplayName = "SQL Agent"
)

$exe = Join-Path $PublishPath "SqlAgent.Host.exe"
if (-not (Test-Path $exe)) {
    throw "Host executable not found at $exe. Publish the host before installing the service."
}

New-Service `
    -Name $ServiceName `
    -DisplayName $DisplayName `
    -BinaryPathName "`"$exe`"" `
    -StartupType Automatic

Start-Service -Name $ServiceName
