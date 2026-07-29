$c = Get-Content 'C:\Users\liufe\.qoderworkcn\workspace\ms5ftxyv4p186r1b\index.html' -Raw
'n = ' + ([regex]::Matches($c, [regex]::Escape('var(--primary-strong)')).Count)
