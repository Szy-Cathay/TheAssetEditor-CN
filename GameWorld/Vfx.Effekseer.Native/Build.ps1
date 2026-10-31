param(
  [Parameter(Mandatory = $true)][string]$EffekseerSource,
  [Parameter(Mandatory = $true)][string]$VisualStudioDirectory,
  [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
  [switch]$NativeTimings,
  [string]$DiagnosticOutputDirectory
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$source = (Resolve-Path -LiteralPath $EffekseerSource).Path
$cmake = Join-Path $VisualStudioDirectory 'Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe'
$ninja = Join-Path $VisualStudioDirectory 'Common7/IDE/CommonExtensions/Microsoft/CMake/Ninja/ninja.exe'
$vcvars = Join-Path $VisualStudioDirectory 'VC/Auxiliary/Build/vcvars64.bat'
foreach ($required in @($cmake, $ninja, $vcvars)) {
  if (!(Test-Path -LiteralPath $required -PathType Leaf)) { throw "Missing build tool: $required" }
}
$compilerEnvironment = & $env:ComSpec /d /s /c "`"$vcvars`" >nul && set"
if ($LASTEXITCODE -ne 0) { throw 'C++ environment initialization failed.' }
foreach ($entry in $compilerEnvironment) {
  $pair = $entry -split '=', 2
  if ($pair[0] -in @('PATH', 'LIB', 'LIBPATH', 'INCLUDE', 'WindowsSdkDir', 'WindowsSDKVersion',
      'VCToolsInstallDir', 'UniversalCRTSdkDir', 'UCRTVersion', 'VCINSTALLDIR', 'VSINSTALLDIR')) {
    [Environment]::SetEnvironmentVariable($pair[0], $pair[1], 'Process')
  }
}
$build = Join-Path $repo "artifacts/effekseer-build-$Configuration"
$timingOutput = ''
if ($NativeTimings) {
  if (!$DiagnosticOutputDirectory) { throw 'NativeTimings requires an explicit fresh artifacts output directory.' }
  $timingOutput = [IO.Path]::GetFullPath($DiagnosticOutputDirectory)
  $artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repo 'artifacts'))
  if (!$timingOutput.StartsWith($artifactsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'NativeTimings output must remain inside this repository artifacts directory.'
  }
  $ancestor = $timingOutput
  while ($ancestor -and $ancestor.Length -ge $artifactsRoot.Length) {
    if ((Test-Path -LiteralPath $ancestor) -and ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
      throw 'NativeTimings output must not traverse a reparse point.'
    }
    $ancestor = [IO.Path]::GetDirectoryName($ancestor)
  }
  if (Test-Path -LiteralPath $timingOutput) {
    throw 'Preserve the existing diagnostic directory; choose a fresh output directory.'
  }
} elseif ($DiagnosticOutputDirectory) {
  throw 'DiagnosticOutputDirectory is only valid with NativeTimings.'
}
$timingSwitch = if ($NativeTimings) { 'ON' } else { 'OFF' }
& $cmake -S $PSScriptRoot -B $build -G Ninja "-DCMAKE_BUILD_TYPE=$Configuration" "-DCMAKE_MAKE_PROGRAM=$ninja" "-DEFFEKSEER_SOURCE_DIR=$source" "-DAECN_VFX_NATIVE_TIMINGS=$timingSwitch" "-DAECN_VFX_DIAGNOSTIC_OUTPUT_DIRECTORY:PATH=$timingOutput"
if ($LASTEXITCODE -ne 0) { throw 'Effekseer configure failed.' }
& $cmake --build $build --parallel 1
if ($LASTEXITCODE -ne 0) { throw 'Effekseer build failed.' }
