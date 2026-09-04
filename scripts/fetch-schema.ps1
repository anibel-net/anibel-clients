#requires -Version 7
# Downloads the live anibel.net GraphQL schema (introspection is public) and
# writes SDL to crates/anibel-api/graphql/schema.graphql — the single source for
# graphql_client codegen (compile-time validation).
$ErrorActionPreference = "Stop"

$introspect = @'
query {
  __schema {
    types {
      kind
      name
      fields {
        name
        args { name type { kind name ofType { kind name ofType { kind name ofType { kind name } } } } }
        type { kind name ofType { kind name ofType { kind name ofType { kind name } } } }
      }
      inputFields {
        name
        type { kind name ofType { kind name ofType { kind name ofType { kind name } } } }
      }
      enumValues { name }
      possibleTypes { name }
    }
  }
}
'@

$body = @{ query = $introspect; variables = @{} } | ConvertTo-Json -Depth 20
$schema = (Invoke-RestMethod -Uri "https://anibel.net/graphql" -Method Post -ContentType "application/json" -Body $body -TimeoutSec 60).data.__schema

if (-not $schema.types) { throw "introspection failed" }

function TypeRef($t) {
    $kind = $t.kind
    if ($kind -eq "NON_NULL") { return (TypeRef ($t.ofType)) + "!" }
    if ($kind -eq "LIST") { return "[" + (TypeRef ($t.ofType)) + "]" }
    return $t.name
}

$sb = [System.Text.StringBuilder]::new()

foreach ($t in $schema.types) {
    $name = $t.name
    if ($name -like "__*") { continue }
    switch ($t.kind) {
        "SCALAR" {
            if ($name -notin "String","Int","Float","Boolean","ID") {
                [void]$sb.AppendLine("scalar $name") }
        }
        "ENUM" {
            [void]$sb.AppendLine("enum $name {")
            foreach ($v in $t.enumValues) { [void]$sb.AppendLine("  $($v.name)") }
            [void]$sb.AppendLine("}")
        }
        "INPUT_OBJECT" {
            [void]$sb.AppendLine("input $name {")
            foreach ($f in $t.inputFields) { [void]$sb.AppendLine("  $($f.name): $(TypeRef $f.type)") }
            [void]$sb.AppendLine("}")
        }
        "OBJECT" {
            [void]$sb.AppendLine("type $name {")
            foreach ($f in $t.fields) {
                $args = ($f.args | ForEach-Object { "$($_.name): $(TypeRef $_.type)" }) -join ", "
                $argStr = if ($args) { "($args)" } else { "" }
                [void]$sb.AppendLine("  $($f.name)$argStr`: $(TypeRef $f.type)")
            }
            [void]$sb.AppendLine("}")
        }
        "UNION" {
            [void]$sb.AppendLine("union $name = $(($t.possibleTypes | ForEach-Object { $_.name }) -join " | ")")
        }
    }
    [void]$sb.AppendLine("")
}

$out = Join-Path $PSScriptRoot "..\crates\anibel-api\graphql\schema.graphql"
New-Item -ItemType Directory -Path (Split-Path $out) -Force | Out-Null
Set-Content $out $sb.ToString()
Write-Host "OK: $out ($($schema.types.Count) types)"
