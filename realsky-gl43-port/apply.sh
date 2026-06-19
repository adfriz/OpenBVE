#!/usr/bin/env bash
# apply.sh — apply the RealSky GL 4.3 port to a checked-out OpenBVE next-gen tree.
#
# Usage:
#   bash apply.sh /path/to/OpenBVE
#
# Or from inside the OpenBVE repo:
#   bash /path/to/realsky-gl43-port/apply.sh .

set -euo pipefail

REPO="${1:-.}"
PORT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if [ ! -d "$REPO/.git" ] && [ ! -f "$REPO/.git" ]; then
    echo "Error: $REPO is not a git repository"
    echo "Usage: $0 /path/to/OpenBVE"
    exit 1
fi

# Verify we're on next-gen (or a descendant)
BRANCH=$(cd "$REPO" && git rev-parse --abbrev-ref HEAD)
if [ "$BRANCH" != "next-gen" ]; then
    echo "Warning: currently on branch '$BRANCH', not 'next-gen'."
    echo "This port is designed for the next-gen branch. Continue anyway? [y/N]"
    read -r response
    if [ "$response" != "y" ] && [ "$response" != "Y" ]; then
        echo "Aborted."
        exit 1
    fi
fi

# Verify working tree is clean
cd "$REPO"
if [ -n "$(git status --porcelain)" ]; then
    echo "Error: working tree is not clean. Please commit or stash changes first."
    git status --short
    exit 1
fi

echo "=== Applying RealSky GL 4.3 port to $REPO ==="
echo "Branch: $BRANCH"
echo "Head:   $(git rev-parse --short HEAD)"
echo ""

# Apply the consolidated patch (single git apply)
PATCH="$PORT_DIR/patches/00_all-files.patch"
echo "Applying: $PATCH"
git apply --whitespace=nowarn "$PATCH"
echo "✓ Applied successfully."
echo ""

# Verify
echo "=== Verification ==="
echo "New files:"
ls -la assets/Shaders/RealSky.comp \
       assets/Shaders/Atmosphere/RealSky.vert \
       assets/Shaders/Atmosphere/RealSky.frag \
       source/LibRender2/Atmosphere/RealSkyComputeShader.cs \
       source/LibRender2/Atmosphere/RealSkyPass.cs 2>/dev/null

echo ""
echo "Modified files:"
git status --short

echo ""
echo "=== Done. Build the project with: ==="
echo "  cd source/OpenBVE && dotnet build -c Release"
echo ""
echo "Then enable RealSky in options.cfg:"
echo "  [RealSky]"
echo "  RealSkyEnabled = true"
echo "  RealSkyAzimuth = 180"
echo "  RealSkyElevation = 45"
