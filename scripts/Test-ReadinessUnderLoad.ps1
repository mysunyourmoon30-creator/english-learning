[CmdletBinding()]
param(
    [uri]$BaseUri = 'http://127.0.0.1:8080',
    [ValidateRange(1, 200)]
    [int]$Concurrency = 20,
    [ValidateRange(1, 100000)]
    [int]$Requests = 500
)

$ErrorActionPreference = 'Stop'
$client = [System.Net.Http.HttpClient]::new()
$client.BaseAddress = $BaseUri
$client.Timeout = [TimeSpan]::FromSeconds(10)
$latencies = [System.Collections.Generic.List[double]]::new()
$failures = [System.Collections.Generic.List[string]]::new()

try {
    for ($offset = 0; $offset -lt $Requests; $offset += $Concurrency) {
        $batchSize = [Math]::Min($Concurrency, $Requests - $offset)
        $batch = for ($index = 0; $index -lt $batchSize; $index++) {
            $started = [Diagnostics.Stopwatch]::StartNew()
            $task = $client.GetAsync('/health/live')
            [pscustomobject]@{ Task = $task; Stopwatch = $started }
        }

        [System.Threading.Tasks.Task]::WaitAll(
            [System.Threading.Tasks.Task[]]@($batch.Task))
        foreach ($request in $batch) {
            $request.Stopwatch.Stop()
            $response = $request.Task.Result
            try {
                $latencies.Add($request.Stopwatch.Elapsed.TotalMilliseconds)
                if (-not $response.IsSuccessStatusCode) {
                    $failures.Add("HTTP $([int]$response.StatusCode)")
                }
            }
            finally {
                $response.Dispose()
            }
        }

        $ready = $client.GetAsync('/health/ready').GetAwaiter().GetResult()
        try {
            if (-not $ready.IsSuccessStatusCode) {
                throw "Readiness failed under load with HTTP $([int]$ready.StatusCode)."
            }
        }
        finally {
            $ready.Dispose()
        }
    }
}
finally {
    $client.Dispose()
}

if ($failures.Count -gt 0) {
    throw "$($failures.Count) of $Requests load requests failed."
}

$sorted = @($latencies | Sort-Object)
$p95Index = [Math]::Min(
    $sorted.Count - 1,
    [Math]::Max(0, [Math]::Ceiling($sorted.Count * 0.95) - 1))
$p95 = $sorted[$p95Index]
Write-Output (
    "Load/readiness smoke passed: {0} requests, concurrency {1}, p95 {2:N1} ms." -f
    $Requests,
    $Concurrency,
    $p95)
