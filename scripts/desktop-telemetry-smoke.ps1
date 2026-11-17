# Run against an already-started isolated API only (never production).
param([string]$BaseUrl = 'http://127.0.0.1:5057')
$ErrorActionPreference = 'Stop'
if ($BaseUrl -notmatch '^http://(127\.0\.0\.1|localhost):[0-9]+$') { throw 'Localhost HTTP API required.' }
$body = '{"release":"0.1.6","os":"windows","arch":"x86_64","events":[{"name":"app_launch","outcome":"success","count":1,"durationMs":125}]}'
function Assert-Status($Body, $Expected) {
    $r = Invoke-WebRequest "$BaseUrl/api/desktop-telemetry" -Method Post -ContentType application/json -Body $Body -SkipHttpErrorCheck
    if ([int]$r.StatusCode -ne $Expected) { throw "Expected $Expected; received $($r.StatusCode)" }
}
Assert-Status $body 204
Assert-Status ($body.Replace('"release":','"email":"never-store@example.invalid","release":')) 400
Assert-Status ($body.Replace('"success"','"arbitrary"')) 400
Assert-Status (' ' * 17000 + $body) 413
$client = [Net.Http.HttpClient]::new()
try {
    $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Post, "$BaseUrl/api/desktop-telemetry")
    $request.Content = [Net.Http.StringContent]::new((' ' * 17000 + $body), [Text.Encoding]::UTF8, 'application/json')
    $request.Headers.TransferEncodingChunked = $true
    $response = $client.SendAsync($request).GetAwaiter().GetResult()
    if ([int]$response.StatusCode -ne 413) { throw "Chunked oversize status: $($response.StatusCode)" }
    $response.Dispose(); $request.Dispose()
} finally { $client.Dispose() }
$metrics = (Invoke-WebRequest "$BaseUrl/metrics").Content
if ($metrics -notmatch 'transcendence_desktop_events_total' -or $metrics -notmatch 'transcendence_desktop_duration_seconds_bucket') { throw 'Missing desktop metrics.' }
if ($metrics -match 'never-store@example.invalid|arbitrary') { throw 'Rejected metadata escaped into metrics.' }
$statuses = 1..12 | ForEach-Object { [int](Invoke-WebRequest "$BaseUrl/api/desktop-telemetry" -Method Post -ContentType application/json -Body $body -SkipHttpErrorCheck).StatusCode }
if ($statuses -notcontains 429) { throw 'Rate limiter did not reject traffic.' }
'PASS: schema, 204/400/413/429, chunked size bound, metrics and rejected metadata.'
