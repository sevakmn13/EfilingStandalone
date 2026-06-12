function Dump-CodeList {
    param([string]$Path, [string]$OutFile)
    $x = [xml](Get-Content $Path)
    $rows = $x.CodeList.SimpleCodeList.Row
    $lines = foreach ($r in $rows) {
        $code = ($r.Value | Where-Object { $_.ColumnRef -eq 'code' }).SimpleValue
        $cv = ($r.Value | Where-Object { $_.ColumnRef -eq 'value' }).ComplexValue
        $name = $cv.name
        "{0,-10} {1}" -f $code, $name
    }
    $lines | Out-File -FilePath $OutFile -Encoding utf8
    Write-Host "[$Path] count=$($lines.Count) -> $OutFile"
}

Dump-CodeList 'docs\fileing files\madera_CASE_CATEGORY.xml' 'scripts\madera_case_category.txt'
Dump-CodeList 'docs\fileing files\madera_CASE_TYPE.xml'     'scripts\madera_case_type.txt'
