param(
  [Parameter(Mandatory = $true)][string]$EffekseerSource,
  [Parameter(Mandatory = $true)][string]$VisualStudioDirectory,
  [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug'
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
& $cmake -S $PSScriptRoot -B $build -G Ninja "-DCMAKE_BUILD_TYPE=$Configuration" "-DCMAKE_MAKE_PROGRAM=$ninja" "-DEFFEKSEER_SOURCE_DIR=$source"
if ($LASTEXITCODE -ne 0) { throw 'Effekseer configure failed.' }
& $cmake --build $build --parallel 1
if ($LASTEXITCODE -ne 0) { throw 'Effekseer build failed.' }
