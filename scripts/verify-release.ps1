[CmdletBinding()]
param(
    [ValidatePattern('^gymlink-release-[a-z0-9-]+$')]
    [string]$ProjectName = 'gymlink-release-audit',
    [ValidateRange(1, 65535)]
    [int]$ApiPort = 62387,
    [ValidateRange(1, 65535)]
    [int]$SqlServerPort = 14331,
    [ValidateRange(1, 65535)]
    [int]$RabbitMqPort = 5673,
    [ValidateRange(1, 65535)]
    [int]$RabbitMqManagementPort = 15673,
    [ValidateRange(1, 65535)]
    [int]$MailpitSmtpPort = 1026,
    [ValidateRange(1, 65535)]
    [int]$MailpitUiPort = 8026,
    [string]$EmulatorId = 'Medium_Phone',
    [string]$ArtifactDirectory = 'artifacts/release-candidate',
    [switch]$SkipStaticVerification,
    [switch]$SkipDocker,
    [switch]$SkipClientLaunch,
    [switch]$KeepStack,
    [switch]$ResetAuditStack
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repositoryRoot = (Resolve-Path (Join-Path $scriptRoot '..')).Path
$composeFile = Join-Path $repositoryRoot 'docker-compose.yml'
$mailpitComposeFile = Join-Path $repositoryRoot 'docker-compose.mailpit.yml'
$environmentFile = Join-Path $repositoryRoot '.env'
$solution = Join-Path $repositoryRoot 'backend/GymLink.sln'
$mobileRoot = Join-Path $repositoryRoot 'ui/gymlink_mobile'
$desktopRoot = Join-Path $repositoryRoot 'ui/gymlink_desktop'
$artifactRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $ArtifactDirectory))
$stackStarted = $false
$referenceStatusBefore = $null

function Write-Step([string]$Message) {
    Write-Host "`n==> $Message" -ForegroundColor Cyan
}

function Invoke-Native {
    param(
        [Parameter(Mandatory)] [string]$Command,
        [Parameter()] [string[]]$Arguments = @(),
        [Parameter()] [string]$WorkingDirectory = $repositoryRoot
    )

    Push-Location $WorkingDirectory
    try {
        & $Command @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "Command failed with exit code ${LASTEXITCODE}: $Command $($Arguments -join ' ')"
        }
    }
    finally {
        Pop-Location
    }
}

function Get-NativeOutput {
    param(
        [Parameter(Mandatory)] [string]$Command,
        [Parameter()] [string[]]$Arguments = @(),
        [Parameter()] [string]$WorkingDirectory = $repositoryRoot
    )

    Push-Location $WorkingDirectory
    try {
        $output = & $Command @Arguments 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "Command failed with exit code ${LASTEXITCODE}: $Command $($Arguments -join ' ')"
        }
        return ($output | Out-String).Trim()
    }
    finally {
        Pop-Location
    }
}

function Import-DotEnv([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw ".env is required. Copy .env.example to .env and supply local-only values."
    }

    foreach ($line in Get-Content -LiteralPath $Path) {
        if ([string]::IsNullOrWhiteSpace($line) -or $line.TrimStart().StartsWith('#')) {
            continue
        }

        $separator = $line.IndexOf('=')
        if ($separator -lt 1) {
            continue
        }

        $name = $line.Substring(0, $separator).Trim()
        $value = $line.Substring($separator + 1).Trim()
        if (($value.StartsWith('"') -and $value.EndsWith('"')) -or
            ($value.StartsWith("'") -and $value.EndsWith("'"))) {
            $value = $value.Substring(1, $value.Length - 2)
        }

        if ([string]::IsNullOrEmpty([Environment]::GetEnvironmentVariable($name, 'Process'))) {
            [Environment]::SetEnvironmentVariable($name, $value, 'Process')
        }
    }
}

function Assert-RequiredEnvironment {
    $required = @(
        'GYMLINK_SQLSERVER_SA_PASSWORD',
        'GYMLINK_RABBITMQ_USERNAME',
        'GYMLINK_RABBITMQ_PASSWORD',
        'Jwt__SigningKey',
        'PasswordReset__CodePepper'
    )

    foreach ($name in $required) {
        $value = [Environment]::GetEnvironmentVariable($name, 'Process')
        if ([string]::IsNullOrWhiteSpace($value) -or $value -match 'replace|placeholder|change-me') {
            throw "Required local variable $name is missing or still contains a placeholder."
        }
    }

    if ($env:GYMLINK_RABBITMQ_USERNAME -eq 'guest') {
        throw 'GYMLINK_RABBITMQ_USERNAME must not be guest.'
    }

    if (-not $SkipDocker) {
        if ($env:Stripe__Enabled -eq 'true') {
            if ($env:Stripe__SecretKey -notmatch '^sk_test_' -or
                $env:Stripe__WebhookSecret -notmatch '^whsec_') {
                throw 'Enabled Stripe must use sk_test_ and whsec_ sandbox credentials.'
            }
        }
    }
}

function Assert-Tool([string]$Name) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "$Name was not found on PATH."
    }
}

function Wait-Until {
    param(
        [Parameter(Mandatory)] [scriptblock]$Condition,
        [Parameter(Mandatory)] [string]$FailureMessage,
        [int]$TimeoutSeconds = 180,
        [int]$IntervalSeconds = 3
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        try {
            if (& $Condition) {
                return
            }
        }
        catch {
            # Readiness probes are expected to fail until their dependency is ready.
        }
        Start-Sleep -Seconds $IntervalSeconds
    } while ([DateTime]::UtcNow -lt $deadline)

    throw $FailureMessage
}

function Test-DockerEngine {
    & docker info *> $null
    return $LASTEXITCODE -eq 0
}

function Ensure-DockerEngine {
    if (Test-DockerEngine) {
        return
    }

    $dockerDesktop = Join-Path $env:ProgramFiles 'Docker/Docker/Docker Desktop.exe'
    if (-not (Test-Path -LiteralPath $dockerDesktop -PathType Leaf)) {
        throw 'Docker Desktop is not running and its executable was not found.'
    }

    Write-Step 'Starting Docker Desktop'
    Start-Process -FilePath $dockerDesktop -WindowStyle Hidden
    Wait-Until -TimeoutSeconds 240 -IntervalSeconds 5 `
        -Condition { Test-DockerEngine } `
        -FailureMessage 'Docker Desktop did not expose a healthy engine within four minutes.'
}

function Set-IsolatedComposeEnvironment {
    $env:GYMLINK_API_PORT = $ApiPort.ToString()
    $env:GYMLINK_SQLSERVER_PORT = $SqlServerPort.ToString()
    $env:GYMLINK_RABBITMQ_PORT = $RabbitMqPort.ToString()
    $env:GYMLINK_RABBITMQ_MANAGEMENT_PORT = $RabbitMqManagementPort.ToString()
    $env:GYMLINK_MAILPIT_SMTP_PORT = $MailpitSmtpPort.ToString()
    $env:GYMLINK_MAILPIT_UI_PORT = $MailpitUiPort.ToString()
    $env:GYMLINK_COMPOSE_SEED_ENABLED = 'true'
    $env:Smtp__Username = 'audit@gymlink.local'
    $env:Smtp__Password = 'audit-only-not-a-real-credential'
    $env:Smtp__SenderEmail = 'audit@gymlink.local'
}

function Get-ComposeArguments {
    return @(
        'compose', '-p', $ProjectName,
        '-f', $composeFile,
        '-f', $mailpitComposeFile
    )
}

function Get-AuditContainerIds {
    $ids = & docker ps -aq --filter "label=com.docker.compose.project=$ProjectName"
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to inspect Docker Compose project labels.'
    }
    return @($ids | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}

function Get-AuditVolumeNames {
    $names = & docker volume ls -q --filter "label=com.docker.compose.project=$ProjectName"
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to inspect Docker Compose volume labels.'
    }
    return @($names | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}

function Remove-AuditStack {
    if ($ProjectName -eq 'gymlink' -or -not $ProjectName.StartsWith('gymlink-release-')) {
        throw "Refusing cleanup for non-audit Compose project '$ProjectName'."
    }

    $ids = Get-AuditContainerIds
    foreach ($id in $ids) {
        $inspection = (& docker inspect $id 2>&1 | Out-String)
        if ($LASTEXITCODE -ne 0) {
            throw "Unable to inspect audit container $id."
        }
        $container = @($inspection | ConvertFrom-Json)[0]
        $label = $container.Config.Labels.'com.docker.compose.project'
        if ($label -ne $ProjectName) {
            throw "Container $id does not belong exclusively to $ProjectName; cleanup refused."
        }
    }

    $volumes = @(Get-AuditVolumeNames)
    foreach ($volume in $volumes) {
        $inspection = (& docker volume inspect $volume 2>&1 | Out-String)
        if ($LASTEXITCODE -ne 0) {
            throw "Unable to inspect audit volume $volume."
        }
        $volumeDetails = @($inspection | ConvertFrom-Json)[0]
        $label = $volumeDetails.Labels.'com.docker.compose.project'
        if ($label -ne $ProjectName) {
            throw "Volume $volume does not belong exclusively to $ProjectName; cleanup refused."
        }
    }

    Invoke-Compose @('down', '--volumes', '--remove-orphans')
}

function Invoke-Compose([string[]]$Arguments) {
    Invoke-Native docker ((Get-ComposeArguments) + $Arguments)
}

function Get-BasicAuthHeaders {
    $raw = '{0}:{1}' -f $env:GYMLINK_RABBITMQ_USERNAME, $env:GYMLINK_RABBITMQ_PASSWORD
    $token = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($raw))
    return @{ Authorization = "Basic $token" }
}

function Invoke-JsonPost([string]$Uri, [object]$Body, [hashtable]$Headers = @{}) {
    return Invoke-RestMethod -Method Post -Uri $Uri -Headers $Headers `
        -ContentType 'application/json' -Body ($Body | ConvertTo-Json -Depth 8 -Compress)
}

function Get-Queue([string]$QueueName) {
    $encodedQueue = [Uri]::EscapeDataString($QueueName)
    return Invoke-RestMethod -Headers (Get-BasicAuthHeaders) `
        -Uri "http://127.0.0.1:$RabbitMqManagementPort/api/queues/%2F/$encodedQueue"
}

function Publish-MalformedMessage([string]$RoutingKey) {
    $body = @{
        properties = @{
            content_type = 'application/json'
            type = 'GymLink.ReleaseAudit.Malformed'
            delivery_mode = 2
        }
        routing_key = $RoutingKey
        payload = '{malformed-release-audit-message'
        payload_encoding = 'string'
    }
    $result = Invoke-JsonPost `
        -Uri "http://127.0.0.1:$RabbitMqManagementPort/api/exchanges/%2F/gymlink.events/publish" `
        -Headers (Get-BasicAuthHeaders) `
        -Body $body
    if (-not $result.routed) {
        throw "Malformed message for $RoutingKey was not routed to a live queue."
    }
}

function Wait-ForMail([string]$Recipient, [int]$MinimumCount = 1) {
    Wait-Until -TimeoutSeconds 120 -IntervalSeconds 3 `
        -Condition {
            $response = Invoke-WebRequest -UseBasicParsing `
                -Uri "http://127.0.0.1:$MailpitUiPort/api/v1/messages"
            $matches = [regex]::Matches($response.Content, [regex]::Escape($Recipient), 'IgnoreCase')
            return $matches.Count -ge $MinimumCount
        } `
        -FailureMessage "Mailpit did not receive a reset email for $Recipient."
}

function Invoke-PasswordResetRequest([string]$Email) {
    $response = Invoke-WebRequest -UseBasicParsing -Method Post `
        -Uri "http://127.0.0.1:$ApiPort/api/auth/forgot-password" `
        -ContentType 'application/json' `
        -Body (@{ email = $Email } | ConvertTo-Json -Compress)
    if ($response.StatusCode -ne 202) {
        throw "Password reset request returned HTTP $($response.StatusCode)."
    }
}

function Assert-SeedLogin([string]$Identifier, [string]$ExpectedRole) {
    $password = if ([string]::IsNullOrWhiteSpace($env:Seed__DefaultPassword)) {
        'Test123!'
    }
    else {
        $env:Seed__DefaultPassword
    }
    $session = Invoke-JsonPost `
        -Uri "http://127.0.0.1:$ApiPort/api/auth/login" `
        -Body @{ identifier = $Identifier; password = $password }
    if (-not $session.accessToken -or $session.user.role -ne $ExpectedRole) {
        throw "Seed login for $Identifier did not return the expected $ExpectedRole role."
    }
}

function Invoke-DatabaseScalar([string]$Query) {
    if ($Query.Contains("'")) {
        throw 'Audit SQL scalar queries must not contain single quotes.'
    }
    $output = Get-NativeOutput docker ((Get-ComposeArguments) + @(
        'exec', '-T', 'sqlserver', 'sh', '-lc',
        "/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P `"`$MSSQL_SA_PASSWORD`" -C -d 230038 -h -1 -W -Q '$Query'"
    ))
    return $output.Trim()
}

function Invoke-StaticVerification {
    Write-Step 'Restoring and verifying the .NET solution'
    Invoke-Native dotnet @('restore', $solution)
    Invoke-Native dotnet @('build', $solution, '-c', 'Release', '--no-restore')
    Invoke-Native dotnet @('test', $solution, '-c', 'Release', '--no-build', '--no-restore')
    Invoke-Native dotnet @('format', $solution, '--verify-no-changes', '--no-restore')
    Invoke-Native dotnet @(
        'ef', 'migrations', 'has-pending-model-changes',
        '--project', (Join-Path $repositoryRoot 'backend/src/GymLink.Infrastructure'),
        '--startup-project', (Join-Path $repositoryRoot 'backend/src/GymLink.Api'),
        '--configuration', 'Release', '--no-build'
    )

    foreach ($clientRoot in @($mobileRoot, $desktopRoot)) {
        Write-Step "Analyzing and testing $([IO.Path]::GetFileName($clientRoot))"
        Invoke-Native flutter @('pub', 'get') $clientRoot
        Invoke-Native dart @('format', '--output=none', '--set-exit-if-changed', 'lib', 'test') $clientRoot
        Invoke-Native flutter @('analyze') $clientRoot
        Invoke-Native flutter @('test') $clientRoot
    }

    Write-Step 'Building Android and Windows release clients'
    Invoke-Native flutter @(
        'build', 'apk', '--release', '--target-platform', 'android-arm64',
        "--dart-define=API_BASE_URL=http://10.0.2.2:$ApiPort"
    ) $mobileRoot
    Invoke-Native flutter @(
        'build', 'windows', '--release',
        "--dart-define=API_BASE_URL=http://localhost:$ApiPort"
    ) $desktopRoot

    Write-Step 'Checking tracked configuration and whitespace'
    $trackedEnv = Get-NativeOutput git @('ls-files', '*.env', '.env')
    if ($trackedEnv -split "`r?`n" | Where-Object { $_ -eq '.env' }) {
        throw '.env is tracked by Git.'
    }
    $forbiddenSecrets = & git grep -n -E '(sk_live_[A-Za-z0-9]+|whsec_[A-Za-z0-9]{16,}|BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY)' -- . `
        ':(exclude)architectureReference/**'
    if ($LASTEXITCODE -eq 0) {
        throw "Potential tracked production secret detected:`n$($forbiddenSecrets | Out-String)"
    }
    if ($LASTEXITCODE -ne 1) {
        throw 'Tracked-secret scan failed.'
    }
    Invoke-Native git @('diff', '--check')
}

function Stage-ReleaseCandidate {
    Write-Step 'Staging ignored local release-candidate artifacts'
    if (-not $artifactRoot.StartsWith($repositoryRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'ArtifactDirectory must resolve inside the GymLink repository.'
    }

    if (Test-Path -LiteralPath $artifactRoot) {
        Remove-Item -LiteralPath $artifactRoot -Recurse -Force
    }
    New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null

    $apkSource = Join-Path $mobileRoot 'build/app/outputs/flutter-apk/app-release.apk'
    $windowsSource = Join-Path $desktopRoot 'build/windows/x64/runner/Release'
    if (-not (Test-Path -LiteralPath $apkSource -PathType Leaf) -or
        -not (Test-Path -LiteralPath $windowsSource -PathType Container)) {
        throw 'Expected Flutter release outputs were not produced.'
    }

    Copy-Item -LiteralPath $apkSource -Destination (Join-Path $artifactRoot 'gymlink-android-arm64.apk')
    $windowsZip = Join-Path $artifactRoot 'gymlink-windows-x64.zip'
    Compress-Archive -Path (Join-Path $windowsSource '*') -DestinationPath $windowsZip -CompressionLevel Optimal

    $metadata = @(
        "createdUtc=$([DateTime]::UtcNow.ToString('O'))"
        "commit=$(Get-NativeOutput git @('rev-parse', 'HEAD'))"
        "dotnet=$(Get-NativeOutput dotnet @('--version'))"
        "flutter=$((Get-NativeOutput flutter @('--version')).Split([Environment]::NewLine)[0])"
        "composeProject=$ProjectName"
        "apiPort=$ApiPort"
        'refunds=explicitly-excluded'
    )
    Set-Content -LiteralPath (Join-Path $artifactRoot 'build-metadata.txt') -Value $metadata -Encoding utf8

    $checksums = Get-ChildItem -LiteralPath $artifactRoot -File |
        Where-Object { $_.Name -ne 'SHA256SUMS.txt' } |
        Sort-Object Name |
        ForEach-Object {
            $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            "$hash  $($_.Name)"
        }
    Set-Content -LiteralPath (Join-Path $artifactRoot 'SHA256SUMS.txt') -Value $checksums -Encoding ascii

    $ignored = & git check-ignore $artifactRoot
    if ($LASTEXITCODE -ne 0) {
        throw "Release candidate directory is not ignored by Git: $artifactRoot"
    }
}

function Invoke-DockerVerification {
    Ensure-DockerEngine
    Set-IsolatedComposeEnvironment

    $existing = @(Get-AuditContainerIds)
    $existingVolumes = @(Get-AuditVolumeNames)
    if ($existing.Count -gt 0 -or $existingVolumes.Count -gt 0) {
        if (-not $ResetAuditStack) {
            throw "The isolated project $ProjectName already has containers. Use -ResetAuditStack to remove only that audit stack."
        }
        Remove-AuditStack
    }

    Write-Step 'Validating and building the isolated Compose stack'
    Invoke-Compose @('config', '--quiet')
    Invoke-Compose @('build', 'api', 'worker')
    $script:stackStarted = $true
    Invoke-Compose @('up', '-d')

    Wait-Until -TimeoutSeconds 300 -IntervalSeconds 5 `
        -Condition {
            $services = Invoke-RestMethod -Uri "http://127.0.0.1:$ApiPort/health"
            return $null -ne $services
        } `
        -FailureMessage 'The Compose API did not become healthy within five minutes.'

    $psJson = Get-NativeOutput docker ((Get-ComposeArguments) + @('ps', '--format', 'json'))
    $serviceRows = @($psJson -split "`r?`n" | Where-Object { $_ } | ForEach-Object { $_ | ConvertFrom-Json })
    foreach ($requiredService in @('sqlserver', 'rabbitmq', 'mailpit', 'api', 'worker')) {
        $row = $serviceRows | Where-Object { $_.Service -eq $requiredService }
        if (-not $row -or $row.State -ne 'running') {
            throw "Compose service $requiredService is not running."
        }
    }

    Write-Step 'Verifying database migration and seeded role access'
    if ((Invoke-DatabaseScalar 'SET NOCOUNT ON; SELECT DB_NAME();') -ne '230038') {
        throw 'The Compose API did not migrate database 230038.'
    }
    $migrationCount = [int](Invoke-DatabaseScalar 'SET NOCOUNT ON; SELECT COUNT(*) FROM [__EFMigrationsHistory];')
    if ($migrationCount -lt 1) {
        throw 'No committed EF Core migrations were applied.'
    }
    Assert-SeedLogin 'mobile1' 'Member'
    Assert-SeedLogin 'arenatrainer1' 'Trainer'
    Assert-SeedLogin 'admin.arena' 'GymAdmin'
    Assert-SeedLogin 'centraladmin' 'CentralAdmin'

    Write-Step 'Verifying API outbox to RabbitMQ to Worker to Mailpit'
    Invoke-PasswordResetRequest 'mobile1@gymlink.local'
    Wait-ForMail 'mobile1@gymlink.local'

    Write-Step 'Verifying committed outbox recovery after a broker outage'
    Invoke-Compose @('stop', 'rabbitmq')
    Invoke-PasswordResetRequest 'mobile2@gymlink.local'
    $pendingOutbox = [int](Invoke-DatabaseScalar 'SET NOCOUNT ON; SELECT COUNT(*) FROM [OutboxMessages] WHERE [PublishedAtUtc] IS NULL;')
    if ($pendingOutbox -lt 1) {
        throw 'The broker outage did not leave committed outbox work pending.'
    }
    Invoke-Compose @('start', 'rabbitmq')
    Wait-Until -TimeoutSeconds 120 -IntervalSeconds 3 `
        -Condition {
            try {
                $overview = Invoke-RestMethod -Headers (Get-BasicAuthHeaders) `
                    -Uri "http://127.0.0.1:$RabbitMqManagementPort/api/overview"
                return $null -ne $overview.rabbitmq_version
            }
            catch { return $false }
        } `
        -FailureMessage 'RabbitMQ Management did not recover after restart.'
    Wait-ForMail 'mobile2@gymlink.local'

    Write-Step 'Verifying RabbitMQ persistence and both poison-message DLQs'
    Invoke-Compose @('stop', 'worker')
    Publish-MalformedMessage 'notification.requested.v1'
    Wait-Until -TimeoutSeconds 30 `
        -Condition { (Get-Queue 'gymlink.notifications.v1').messages_ready -ge 1 } `
        -FailureMessage 'The malformed notification was not retained in the live queue.'
    Invoke-Compose @('restart', 'rabbitmq')
    Wait-Until -TimeoutSeconds 120 -IntervalSeconds 3 `
        -Condition {
            try { return (Get-Queue 'gymlink.notifications.v1').messages_ready -ge 1 }
            catch { return $false }
        } `
        -FailureMessage 'The live notification queue did not survive a RabbitMQ restart.'
    Invoke-Compose @('start', 'worker')
    Wait-Until -TimeoutSeconds 60 -IntervalSeconds 2 `
        -Condition { (Get-Queue 'gymlink.notifications.dead-letter.v1').messages_ready -ge 1 } `
        -FailureMessage 'The malformed notification did not reach its DLQ.'

    Publish-MalformedMessage 'password-reset.requested.v1'
    Wait-Until -TimeoutSeconds 60 -IntervalSeconds 2 `
        -Condition { (Get-Queue 'gymlink.email.dead-letter.v1').messages_ready -ge 1 } `
        -FailureMessage 'The malformed reset email did not reach its DLQ.'

    Write-Step 'Verifying SQL Server persistence across restart'
    Invoke-Compose @('restart', 'sqlserver')
    Wait-Until -TimeoutSeconds 180 -IntervalSeconds 5 `
        -Condition {
            try { return (Invoke-DatabaseScalar 'SET NOCOUNT ON; SELECT DB_NAME();') -eq '230038' }
            catch { return $false }
        } `
        -FailureMessage 'Database 230038 did not recover after SQL Server restart.'
    Wait-Until -TimeoutSeconds 120 -IntervalSeconds 3 `
        -Condition {
            try {
                $health = Invoke-RestMethod -Uri "http://127.0.0.1:$ApiPort/health"
                return $null -ne $health
            }
            catch { return $false }
        } `
        -FailureMessage 'The Compose API did not recover after SQL Server restart.'
    Assert-SeedLogin 'centraladmin' 'CentralAdmin'
}

function Invoke-ClientLaunchSmoke {
    if ($SkipClientLaunch -or $SkipDocker) {
        return
    }

    Write-Step 'Launching client startup smokes'
    $emulators = Get-NativeOutput flutter @('emulators')
    if ($emulators -notmatch [regex]::Escape($EmulatorId)) {
        throw "Android emulator '$EmulatorId' is not available."
    }
    Invoke-Native flutter @('emulators', '--launch', $EmulatorId)
    Wait-Until -TimeoutSeconds 180 -IntervalSeconds 5 `
        -Condition { (Get-NativeOutput flutter @('devices')) -match 'android' } `
        -FailureMessage "Android emulator '$EmulatorId' did not become available."

    $androidDeviceLine = (Get-NativeOutput flutter @('devices')) -split "`r?`n" |
        Where-Object { $_ -match 'android' } | Select-Object -First 1
    $androidDeviceId = ($androidDeviceLine -split '\s+•\s+')[1].Trim()
    Invoke-Native flutter @(
        'install', '-d', $androidDeviceId,
        "--use-application-binary=$(Join-Path $artifactRoot 'gymlink-android-arm64.apk')"
    ) $mobileRoot

    $desktopExe = Join-Path $desktopRoot 'build/windows/x64/runner/Release/gymlink_desktop.exe'
    if (-not (Test-Path -LiteralPath $desktopExe -PathType Leaf)) {
        throw 'The Windows release executable was not produced.'
    }
    $desktopProcess = Start-Process -FilePath $desktopExe -PassThru
    Start-Sleep -Seconds 8
    if ($desktopProcess.HasExited) {
        throw 'The Windows release client exited during startup smoke.'
    }
    Stop-Process -Id $desktopProcess.Id
    Write-Host 'Android APK installed and Windows release client remained running through startup.'
}

try {
    Set-Location $repositoryRoot
    Assert-Tool git
    Assert-Tool dotnet
    Assert-Tool flutter
    Assert-Tool dart
    if (-not $SkipDocker) {
        Assert-Tool docker
    }

    Import-DotEnv $environmentFile
    Assert-RequiredEnvironment
    if (Test-Path -LiteralPath (Join-Path $repositoryRoot 'architectureReference/.git')) {
        $referenceStatusBefore = Get-NativeOutput git @('-C', 'architectureReference', 'status', '--porcelain=v1')
    }

    if (-not $SkipStaticVerification) {
        Invoke-StaticVerification
    }
    Stage-ReleaseCandidate
    if (-not $SkipDocker) {
        Invoke-DockerVerification
    }
    Invoke-ClientLaunchSmoke

    if ($null -ne $referenceStatusBefore) {
        $referenceStatusAfter = Get-NativeOutput git @('-C', 'architectureReference', 'status', '--porcelain=v1')
        if ($referenceStatusAfter -ne $referenceStatusBefore) {
            throw 'architectureReference changed during release verification.'
        }
    }

    Write-Step 'Phase 12 release verification passed'
    Write-Host "Local release candidate: $artifactRoot"
}
finally {
    if ($stackStarted -and -not $KeepStack) {
        Write-Step "Removing isolated Compose project $ProjectName"
        Remove-AuditStack
    }
}
