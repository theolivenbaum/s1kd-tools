#!/usr/bin/env bash
#
# Packs Tesserae.Pdf into .localfeed, so the editor front-end can be built.
#
# The page preview renders with Tesserae.Pdf (a Tesserae wrapper around Mozilla's
# pdf.js), which is not on nuget.org at the time of writing. NuGet.config adds
# .localfeed as a package source and tolerates it being absent, so this script is
# needed exactly until the package is published — after which it does nothing that
# a plain restore would not.
#
# Only the Transpose half of the repository needs it: S1kdTools.slnx — the library,
# the CLI, the tests and the editor back-end — restores from nuget.org alone.
#
#   ./samples/editor/pack-tesserae-pdf.sh
#   dotnet build S1kdTools.Editor.slnx
#
# Node is a prerequisite: Tesserae.Pdf bundles pdf.js from a pinned npm package
# rather than vendoring it.

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
checkout="${TESSERAE_PDF_DIR:-${TMPDIR:-/tmp}/tesserae-pdf}"
feed="$repo_root/.localfeed"

if [ -d "$checkout/.git" ]; then
    echo "Updating $checkout"
    git -C "$checkout" pull --ff-only
else
    echo "Cloning tesserae-pdf into $checkout"
    git clone --depth 1 https://github.com/curiosity-ai/tesserae-pdf "$checkout"
fi

# Pack in two steps rather than one: `dotnet pack` alone runs the Transpose
# compiler through a path that leaves the assembly where pack does not look for it.
dotnet build "$checkout/Tesserae.Pdf/Tesserae.Pdf.csproj" -c Release
dotnet pack  "$checkout/Tesserae.Pdf/Tesserae.Pdf.csproj" -c Release --no-build -o "$feed"

echo
echo "Packed into $feed:"
ls -1 "$feed"
