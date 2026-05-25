#!/bin/bash
set -euo pipefail

cd "$CLAUDE_PROJECT_DIR"

# Only run in Claude Code on the web (remote) environments.
if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

# Isolated virtualenv keeps installs reproducible across fresh containers.
python3 -m venv .venv
.venv/bin/pip install --upgrade pip >/dev/null 2>&1 || true
.venv/bin/pip install -r requirements.txt

# Browser used by the crawlers. Requires the environment to allow outbound
# network access (cdn.playwright.dev). If the network policy blocks it, this
# step is skipped so session startup still succeeds.
.venv/bin/playwright install chromium \
  || echo "[session-start] playwright browser install skipped (network blocked?)"

# Persist tooling for the rest of the session.
{
  echo "export PATH=\"$CLAUDE_PROJECT_DIR/.venv/bin:\$PATH\""
  echo "export PYTHONPATH=\"$CLAUDE_PROJECT_DIR\""
} >> "$CLAUDE_ENV_FILE"
