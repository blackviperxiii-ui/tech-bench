# Fail-closed: TechBench.exe must be 32-bit i386 (PE Machine 0x014C).
# RP1210 adapter drivers will not load into x64 or AnyCPU.
# Keep /platform:x86 in build.bat / test.bat.
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$Path
)

$ErrorActionPreference = 'Stop'

function Fail([string]$Message) {
    Write-Host "ERROR: $Message"
    exit 1
}

if (-not (Test-Path -LiteralPath $Path)) {
    Fail "Missing $Path"
}

$full = (Resolve-Path -LiteralPath $Path).Path
$bytes = [IO.File]::ReadAllBytes($full)
if ($bytes.Length -lt 64 -or $bytes[0] -ne 0x4D -or $bytes[1] -ne 0x5A) {
    Fail "Not a PE (missing MZ): $full"
}

$pe = [BitConverter]::ToInt32($bytes, 0x3C)
if ($pe -lt 0 -or ($pe + 6) -gt $bytes.Length) {
    Fail "Invalid e_lfanew $pe in $full"
}

$sig = [Text.Encoding]::ASCII.GetString($bytes, $pe, 4)
if ($sig -ne "PE`0`0") {
    Fail "Missing PE signature in $full"
}

$machine = [BitConverter]::ToUInt16($bytes, $pe + 4)
$hex = '0x{0:X4}' -f $machine
if ($machine -ne 0x014C) {
    Fail "PE Machine=$hex — expected 0x014C (i386). x64/AnyCPU cannot load RP1210. Rebuild with /platform:x86."
}

# AnyCPU is also I386 in the Machine field. Require a real x86 assembly.
$arch = [Reflection.AssemblyName]::GetAssemblyName($full).ProcessorArchitecture
if ($arch -ne [Reflection.ProcessorArchitecture]::X86) {
    Fail "ProcessorArchitecture=$arch — expected X86 (not AnyCPU/x64). Rebuild with /platform:x86."
}

Write-Host "OK PE Machine=0x014C (i386), ProcessorArchitecture=X86: $full"
exit 0
