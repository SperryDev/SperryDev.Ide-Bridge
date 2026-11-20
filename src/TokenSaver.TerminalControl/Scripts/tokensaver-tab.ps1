# Runs inside one Windows Terminal tab for a TokenSaver standard session.
# Reads "<id> <command>" lines from the command pipe (TokenSaver is the server), runs each command,
# and writes to the stdout pipe (TokenSaver is the server):
#   plain output lines                    -> stdout for the current command
#   __TOKENSAVER_ERR__ <id> <text>        -> error records, in order with stdout
#   __TOKENSAVER_END__ <id> <exitcode>    -> end of the command
# The reserved command "__TOKENSAVER_CLOSE__" ends the loop, which closes the wt tab.
param(
    [Parameter(Mandatory)][string]$StdoutPipe,
    [Parameter(Mandatory)][string]$CommandPipe
)

$ErrorActionPreference = 'Continue'
$endMarker = '__TOKENSAVER_END__'
$errorMarker = '__TOKENSAVER_ERR__'
$closeCommand = '__TOKENSAVER_CLOSE__'

# Output pipe is created by TokenSaver (server side); this tab connects as a client.
$outClient = [System.IO.Pipes.NamedPipeClientStream]::new('.', $StdoutPipe, [System.IO.Pipes.PipeDirection]::Out)
$outClient.Connect(60000)
$outW = [System.IO.StreamWriter]::new($outClient)
$outW.AutoFlush = $true
$outW.NewLine = "`n"

# Command pipe is created by TokenSaver (server side); this tab connects as a client.
$cmdClient = [System.IO.Pipes.NamedPipeClientStream]::new('.', $CommandPipe, [System.IO.Pipes.PipeDirection]::In)
$cmdClient.Connect(60000)
$cmdReader = [System.IO.StreamReader]::new($cmdClient)

while ($null -ne ($line = $cmdReader.ReadLine())) {
    $space = $line.IndexOf(' ')
    if ($space -lt 1) { continue }

    $id = $line.Substring(0, $space)
    $command = $line.Substring($space + 1)

    if ($command -eq $closeCommand) {
        $outW.WriteLine("$endMarker $id 0")
        break
    }

    $global:LASTEXITCODE = 0
    $failed = $false
    try {
        & ([scriptblock]::Create($command)) 2>&1 | ForEach-Object {
            if ($_ -is [System.Management.Automation.ErrorRecord]) {
                # Error text must stay on one line so the protocol framing holds.
                $text = ($_.ToString() -replace '[\r\n]+', ' | ')
                $outW.WriteLine("$errorMarker $id $text")
            }
            else {
                $outW.WriteLine([string]$_)
            }
        }
    }
    catch {
        $failed = $true
        $text = ($_.ToString() -replace '[\r\n]+', ' | ')
        $outW.WriteLine("$errorMarker $id $text")
    }

    if ($failed) { $code = 1 }
    elseif ($global:LASTEXITCODE -is [int] -and $global:LASTEXITCODE -ne 0) { $code = $global:LASTEXITCODE }
    else { $code = 0 }

    $outW.WriteLine("$endMarker $id $code")
}

$cmdReader.Dispose()
$cmdClient.Dispose()
$outW.Dispose()
$outClient.Dispose()
