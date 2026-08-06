param(
    [string]$SourceDir,
    [string]$OutFile
)

$ErrorActionPreference = "Stop"

$root = [System.IO.Path]::GetFullPath($SourceDir)
$dirId = @{}
$dirCounter = 0
$compCounter = 0

$xml = New-Object System.Text.StringBuilder
[void]$xml.AppendLine('<?xml version="1.0" encoding="utf-8"?>')
[void]$xml.AppendLine('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">')
[void]$xml.AppendLine('  <Fragment>')
[void]$xml.AppendLine('    <StandardDirectory Id="ProgramFiles6432Folder">')

function Get-Indent([int]$depth) { return ("  " * ($depth + 2)) }

function Emit-Dirs {
    param([System.IO.DirectoryInfo]$dir, [int]$depth)

    $myId = $dirId[$dir.FullName]
    [void]$xml.AppendLine("$(Get-Indent $depth)<Directory Id=`"$myId`" Name=`"$($dir.Name)`">")

    foreach ($sub in $dir.GetDirectories()) {
        $script:dirCounter++
        $dirId[$sub.FullName] = "dir_$script:dirCounter"
        Emit-Dirs $sub ($depth + 1)
    }

    [void]$xml.AppendLine("$(Get-Indent $depth)</Directory>")
}

$rootDir = [System.IO.DirectoryInfo]::new($root)
$dirId[$rootDir.FullName] = "INSTALLFOLDER"
[void]$xml.AppendLine("    <Directory Id=`"INSTALLFOLDER`" Name=`"SafeDisk Cleaner`">")
foreach ($sub in $rootDir.GetDirectories()) {
    $script:dirCounter++
    $dirId[$sub.FullName] = "dir_$script:dirCounter"
    Emit-Dirs $sub 1
}
[void]$xml.AppendLine("    </Directory>")
[void]$xml.AppendLine('    </StandardDirectory>')

[void]$xml.AppendLine('    <ComponentGroup Id="AppFiles">')

$files = Get-ChildItem -Path $root -Recurse -File
foreach ($file in $files) {
    $dir = $dirId[$file.Directory.FullName]
    $rel = $file.FullName.Substring($root.Length).TrimStart('\')
    [void]$xml.AppendLine("      <Component Id=`"cmp_$script:compCounter`" Directory=`"$dir`">")
    [void]$xml.AppendLine("        <File Id=`"fil_$script:compCounter`" Source=`"app\$rel`" KeyPath=`"yes`" />")
    [void]$xml.AppendLine("      </Component>")
    $script:compCounter++
}

[void]$xml.AppendLine('    </ComponentGroup>')
[void]$xml.AppendLine('  </Fragment>')
[void]$xml.AppendLine('</Wix>')

$xml.ToString() | Set-Content -Path $OutFile -Encoding UTF8
Write-Host "Generated $OutFile with $compCounter files"
