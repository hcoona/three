Invoke-WebRequest `
  -UseBasicParsing `
  -Uri "https://www.oxfordlearnersdictionaries.com/wordlists/oxford3000-5000" `
  -TimeoutSec 5 `
  -MaximumRetryCount 3 `
  -RetryIntervalSec 1 `
  -Method GET `
  -OutFile "${PSScriptRoot}\oxford3000-5000.html"
