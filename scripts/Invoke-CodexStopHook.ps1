#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$BridgePath)

$ErrorActionPreference = 'Stop'
$document = $null
try {
    # Stop provides JSON on stdin, avoiding Windows' command-line limit before
    # the bridge can start. Do not read or persist the transcript/prompt fields.
    $reader = [IO.StreamReader]::new([Console]::OpenStandardInput(), [Text.UTF8Encoding]::new($false, $true))
    $buffer = [char[]]::new(4096)
    $inputBuilder = [Text.StringBuilder]::new()
    while (($count = $reader.Read($buffer, 0, $buffer.Length)) -gt 0) {
        if ($inputBuilder.Length + $count -gt 2097152) { throw 'STOP_INPUT_TOO_LARGE' }
        [void]$inputBuilder.Append($buffer, 0, $count)
    }
    [Array]::Clear($buffer, 0, $buffer.Length)
    $document = [Text.Json.JsonDocument]::Parse($inputBuilder.ToString())
    $root = $document.RootElement
    if ($root.ValueKind -ne [Text.Json.JsonValueKind]::Object) { throw 'STOP_INPUT_INVALID' }

    function Read-UniqueString([Text.Json.JsonElement]$Object, [string]$Name) {
        $found = $false
        $value = $null
        foreach ($property in $Object.EnumerateObject()) {
            if ($property.Name -cne $Name) { continue }
            if ($found) { throw 'STOP_DUPLICATE_FIELD' }
            $found = $true
            if ($property.Value.ValueKind -eq [Text.Json.JsonValueKind]::String) {
                $value = $property.Value.GetString()
            }
        }
        return $value
    }

    $eventName = Read-UniqueString $root 'hook_event_name'
    if ($eventName -ceq 'Stop') {
        $threadId = Read-UniqueString $root 'session_id'
        $turnId = Read-UniqueString $root 'turn_id'
        foreach ($id in @($threadId, $turnId)) {
            if ($null -eq $id -or $id.Length -lt 1 -or $id.Length -gt 256 -or $id -match '\p{Cc}') {
                throw 'STOP_ID_INVALID'
            }
        }
        $preview = $null
        $answer = Read-UniqueString $root 'last_assistant_message'
        if ($null -ne $answer -and $answer.Trim().StartsWith('<heartbeat>', [StringComparison]::Ordinal)) {
            $notify = $false
            $xmlReader = $null
            try {
                $settings = [Xml.XmlReaderSettings]::new()
                $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
                $settings.XmlResolver = $null
                $settings.MaxCharactersInDocument = 2097152
                $xmlReader = [Xml.XmlReader]::Create([IO.StringReader]::new($answer.Trim()), $settings)
                $heartbeat = [Xml.Linq.XDocument]::Load($xmlReader).Root
                if ($heartbeat.Name -eq [Xml.Linq.XName]'heartbeat') {
                    $decisions = @($heartbeat.Elements([Xml.Linq.XName]'decision'))
                    $messages = @($heartbeat.Elements([Xml.Linq.XName]'message'))
                    $ids = @($heartbeat.Elements([Xml.Linq.XName]'automation_id'))
                    if ($ids.Count -eq 1 -and -not [string]::IsNullOrWhiteSpace($ids[0].Value) -and
                        $decisions.Count -eq 1 -and $decisions[0].Value.Trim() -ceq 'NOTIFY' -and
                        $messages.Count -eq 1 -and -not [string]::IsNullOrWhiteSpace($messages[0].Value)) {
                        $answer = $messages[0].Value
                        $notify = $true
                    }
                }
            } catch [Xml.XmlException] {
                $notify = $false
            } finally {
                if ($null -ne $xmlReader) { $xmlReader.Dispose() }
            }
            if (-not $notify) {
                [Console]::Out.WriteLine('{}')
                exit 0
            }
        }
        if ($null -ne $answer) {
            $normalized = [regex]::Replace($answer.Normalize([Text.NormalizationForm]::FormC), '[\s\p{Cc}\u061c\u200e-\u200f\u202a-\u202e\u2066-\u2069]+', ' ').Trim()
            $indexes = [Globalization.StringInfo]::ParseCombiningCharacters($normalized)
            if ($indexes.Length -gt 50) { $normalized = $normalized.Substring(0, $indexes[50]) + '…' }
            if ($normalized.Length -gt 0 -and $normalized.Length -le 1024) { $preview = $normalized }
        }
        $payload = [ordered]@{
            'type' = 'agent-turn-complete'
            'thread-id' = $threadId
            'turn-id' = $turnId
            'last-assistant-message' = $preview
        } | ConvertTo-Json -Compress
        if ($payload.Length -gt 8192) { throw 'STOP_FORWARD_TOO_LARGE' }

        $start = [Diagnostics.ProcessStartInfo]::new()
        $start.FileName = [IO.Path]::GetFullPath($BridgePath)
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $start.ArgumentList.Add('hook')
        $start.ArgumentList.Add($payload)
        $process = [Diagnostics.Process]::Start($start)
        try {
            if (-not $process.WaitForExit(10000)) { throw 'STOP_BRIDGE_WAIT_TIMEOUT' }
            if ($process.ExitCode -ne 0) { throw 'STOP_BRIDGE_FAILED' }
        } finally { $process.Dispose() }
    }
} catch {
    # No content or exception message is written to stdout/stderr. Fail open:
    # never block/continue the user's Codex turn because a notifier failed.
    [Console]::Error.WriteLine('CODEX_TELEGRAM_STOP_CAPTURE_FAILED')
} finally {
    if ($null -ne $document) { $document.Dispose() }
}
[Console]::Out.WriteLine('{}')
exit 0
