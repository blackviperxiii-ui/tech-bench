# Fail-closed: TechBench.exe must be 32-bit i386 (PE Machine 0x014C) with
# CLR 32BITREQUIRED. RP1210 adapter drivers will not load into x64 or AnyCPU.
# Keep /platform:x86 in build.bat / test.bat.
#
# Do not use [Reflection.AssemblyName]::GetAssemblyName — pwsh 7 reports
# ProcessorArchitecture=None for this Framework 4 winexe, while Windows
# PowerShell 5.1 reports X86. CorFlags is the same on both.
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$Path
)

$ErrorActionPreference = 'Stop'

function Fail([string]$Message) {
    Write-Host "ERROR: $Message"
    exit 1
}

function Read-U16([byte[]]$Bytes, [int]$Offset) {
    return [BitConverter]::ToUInt16($Bytes, $Offset)
}

function Read-I32([byte[]]$Bytes, [int]$Offset) {
    return [BitConverter]::ToInt32($Bytes, $Offset)
}

function Read-U32([byte[]]$Bytes, [int]$Offset) {
    return [BitConverter]::ToUInt32($Bytes, $Offset)
}

if (-not (Test-Path -LiteralPath $Path)) {
    Fail "Missing $Path"
}

$full = (Resolve-Path -LiteralPath $Path).Path
$bytes = [IO.File]::ReadAllBytes($full)
if ($bytes.Length -lt 64 -or $bytes[0] -ne 0x4D -or $bytes[1] -ne 0x5A) {
    Fail "Not a PE (missing MZ): $full"
}

$pe = Read-I32 $bytes 0x3C
if ($pe -lt 0 -or ($pe + 24) -gt $bytes.Length) {
    Fail "Invalid e_lfanew $pe in $full"
}

$sig = [Text.Encoding]::ASCII.GetString($bytes, $pe, 4)
if ($sig -ne "PE`0`0") {
    Fail "Missing PE signature in $full"
}

$machine = Read-U16 $bytes ($pe + 4)
$hex = '0x{0:X4}' -f $machine
if ($machine -ne 0x014C) {
    Fail "PE Machine=$hex — expected 0x014C (i386). x64/AnyCPU cannot load RP1210. Rebuild with /platform:x86."
}

$numberOfSections = Read-U16 $bytes ($pe + 6)
$sizeOfOptional = Read-U16 $bytes ($pe + 20)
$opt = $pe + 24
if (($opt + $sizeOfOptional) -gt $bytes.Length) {
    Fail "Optional header overruns file"
}

$magic = Read-U16 $bytes $opt
if ($magic -eq 0x20B) {
    Fail "PE32+ optional header (x64). Rebuild with /platform:x86."
}
if ($magic -ne 0x10B) {
    Fail ("Unknown optional header magic 0x{0:X4}" -f $magic)
}

# PE32: data directories begin at optional header + 96.
$dataDirs = $opt + 96
if (($dataDirs + 15 * 8) -gt $bytes.Length) {
    Fail "Data directories overrun file"
}

$numRva = Read-U32 $bytes ($dataDirs - 4)
if ($numRva -lt 15) {
    Fail "No COM descriptor directory — not a managed x86 build?"
}

$comRva = Read-U32 $bytes ($dataDirs + 14 * 8)
$comSize = Read-U32 $bytes ($dataDirs + 14 * 8 + 4)
if ($comRva -eq 0 -or $comSize -lt 20) {
    Fail "Missing CLR header (not a managed x86 build?)"
}

$sectionStart = $opt + $sizeOfOptional
$fileOffset = -1
for ($i = 0; $i -lt $numberOfSections; $i++) {
    $s = $sectionStart + $i * 40
    if (($s + 24) -gt $bytes.Length) { Fail "Section header overruns file" }
    $virtSize = Read-U32 $bytes ($s + 8)
    $va = Read-U32 $bytes ($s + 12)
    $rawSize = Read-U32 $bytes ($s + 16)
    $rawPtr = Read-U32 $bytes ($s + 20)
    $span = $virtSize
    if ($rawSize -gt $span) { $span = $rawSize }
    if ($comRva -ge $va -and $comRva -lt ($va + $span)) {
        $fileOffset = [int]($rawPtr + ($comRva - $va))
        break
    }
}
if ($fileOffset -lt 0 -or ($fileOffset + 20) -gt $bytes.Length) {
    Fail ("CLR header RVA 0x{0:X} not in any section" -f $comRva)
}

$flags = Read-U32 $bytes ($fileOffset + 16)
$flag32Required = 0x2
if (($flags -band $flag32Required) -eq 0) {
    Fail ("CLR Flags=0x{0:X} missing 32BITREQUIRED — AnyCPU cannot load RP1210. Rebuild with /platform:x86." -f $flags)
}

Write-Host ("OK PE Machine=0x014C (i386), CLR 32BITREQUIRED, Flags=0x{0:X}: {1}" -f $flags, $full)
exit 0
