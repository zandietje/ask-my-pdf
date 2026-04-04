#!/bin/bash
set -e

# Restore Claude CLI config from backup if the main config is missing.
# The volume persists /root/.claude/ (including backups), but /root/.claude.json
# lives in the home dir root and gets lost on container recreation.
CONFIG="$HOME/.claude.json"
BACKUP_DIR="$HOME/.claude/backups"

if [ ! -f "$CONFIG" ] && [ -d "$BACKUP_DIR" ]; then
    LATEST=$(ls -t "$BACKUP_DIR"/.claude.json.backup.* 2>/dev/null | head -1)
    if [ -n "$LATEST" ]; then
        echo "Restoring Claude CLI config from backup: $LATEST"
        cp "$LATEST" "$CONFIG"
    fi
fi

exec "$@"
