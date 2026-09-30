param([Parameter(Mandatory)][string]$EvidenceRoot)
$ErrorActionPreference = 'Stop'
$results = @()
foreach ($file in Get-ChildItem -LiteralPath $EvidenceRoot -Filter '*.pdb' -Recurse) {
    $stream = [System.IO.File]::OpenRead($file.FullName)
    $provider = [System.Reflection.Metadata.MetadataReaderProvider]::FromPortablePdbStream($stream)
    try {
        $reader = $provider.GetMetadataReader([System.Reflection.Metadata.MetadataReaderOptions]::Default, $null)
        $documents = @()
        foreach ($handle in $reader.Documents) {
            $document = $reader.GetDocument($handle)
            $documents += $reader.GetString($document.Name)
        }
        $results += @{ file = $file.FullName; kind = 'portable-pdb'; documents = $documents }
    }
    finally { $provider.Dispose(); $stream.Dispose() }
}
foreach ($file in Get-ChildItem -LiteralPath $EvidenceRoot -Filter '*.dll' -Recurse) {
    $stream = [System.IO.File]::OpenRead($file.FullName)
    $reader = [System.Reflection.PortableExecutable.PEReader]::new($stream)
    try {
        $entries = @()
        foreach ($entry in $reader.ReadDebugDirectory()) {
            if ($entry.Type -eq [System.Reflection.PortableExecutable.DebugDirectoryEntryType]::CodeView) {
                $data = $reader.ReadCodeViewDebugDirectoryData($entry)
                $entries += @{ path = $data.Path; guid = $data.Guid.ToString(); age = $data.Age }
            }
        }
        $results += @{ file = $file.FullName; kind = 'pe-codeview'; entries = $entries }
    }
    finally { $reader.Dispose(); $stream.Dispose() }
}
$results | ConvertTo-Json -Depth 8
