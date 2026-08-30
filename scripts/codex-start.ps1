$ErrorActionPreference = "Stop"
$Repo = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$Prompt = "请作为本仓库的主协调Codex线程，先读取 .codex/START_HERE.md 和 AGENTS.md，运行 python scripts/validate_handoff.py；不要立即大规模编码。"
$EncodedPath = [System.Uri]::EscapeDataString($Repo)
$EncodedPrompt = [System.Uri]::EscapeDataString($Prompt)
$Link = "codex://new?path=$EncodedPath&prompt=$EncodedPrompt"
Write-Host $Link
try { Start-Process $Link } catch { Write-Warning "无法自动打开Codex，请复制上面的深度链接。" }
