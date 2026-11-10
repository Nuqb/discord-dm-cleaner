param([switch]$Test, [switch]$Package)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    $version = (Get-Content VERSION -Raw).Trim()
    if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid VERSION' }
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
    if (!(Test-Path $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe' }
    if (!(Test-Path $compiler)) { throw 'Install .NET Framework 4.x (including the C# compiler) on Windows.' }
    New-Item -ItemType Directory -Force artifacts/bin,artifacts/test-output,artifacts/release | Out-Null
    $refs = @('/resource:assets/cleaner.ico,DiscordDmCleaner.Icon', '/r:System.Net.Http.dll', '/r:System.Web.Extensions.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll')
    $sources = @(Get-ChildItem src/*.cs | ForEach-Object FullName)
    & $compiler /nologo /target:winexe /platform:anycpu /optimize+ /main:DiscordDmCleaner.CleanerWindow /win32icon:assets/cleaner.ico /out:artifacts/bin/DiscordDmCleaner.exe @refs @sources
    if ($LASTEXITCODE -ne 0) { throw 'Application compilation failed' }
    if ($Test -or $Package) {
        & $compiler /nologo /target:exe /main:CleanerTests /out:artifacts/test-output/CleanerTests.exe @refs @sources (Join-Path $root 'tests\CleanerTests.cs')
        if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed' }
        & ./artifacts/test-output/CleanerTests.exe
        if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
    }
    if ($Package) {
        $stage = "artifacts/release/Discord-DM-Cleaner-v$version-Windows"
        if (Test-Path $stage) { throw "Package staging directory exists: $stage. Use a clean checkout or remove that directory before packaging again." }
        New-Item -ItemType Directory $stage | Out-Null
        Copy-Item artifacts/bin/DiscordDmCleaner.exe $stage
        Copy-Item docs/START-HERE.txt $stage
        Copy-Item LICENSE,NOTICE $stage
        Copy-Item docs/RELEASE-NOTES.md "$stage/RELEASE-NOTES.txt"
        $zip = "artifacts/release/Discord-DM-Cleaner-v$version-Windows.zip"
        Compress-Archive -Path "$stage/*" -DestinationPath $zip -Force
        $hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $(Split-Path $zip -Leaf)" | Set-Content "artifacts/release/SHA256SUMS.txt" -Encoding ascii
        Write-Host "Release ready: $zip"
    }
} finally { Pop-Location }
