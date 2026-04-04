#!/bin/bash
set -e

# Persist /root/.claude.json across container recreations.
# The volume mounts /root/.claude/ but .claude.json lives in the home dir root.
# Solution: store the real file inside the volume, symlink from home dir.
PERSIST_DIR="$HOME/.claude"
CONFIG="$HOME/.claude.json"
PERSISTED="$PERSIST_DIR/.claude.json.persisted"

mkdir -p "$PERSIST_DIR"

if [ -L "$CONFIG" ]; then
    # Already a symlink (e.g. container restart without recreation) — nothing to do
    :
elif [ -f "$CONFIG" ]; then
    # Real file exists (fresh login happened) — move into volume and symlink
    mv "$CONFIG" "$PERSISTED"
    ln -s "$PERSISTED" "$CONFIG"
elif [ -f "$PERSISTED" ]; then
    # Container recreated but persisted copy exists — restore symlink
    ln -s "$PERSISTED" "$CONFIG"
fi

exec "$@"
