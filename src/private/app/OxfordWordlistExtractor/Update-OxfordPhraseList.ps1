Invoke-WebRequest `
  -UseBasicParsing `
  -Uri "https://www.oxfordlearnersdictionaries.com/wordlists/oxford-phrase-list" `
  -TimeoutSec 5 `
  -MaximumRetryCount 3 `
  -RetryIntervalSec 1 `
  -Method GET `
  -OutFile "${PSScriptRoot}\oxford-phrase-list.html"
