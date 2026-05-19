#!/usr/bin/env bash
# Download ONNX model files for ApartmentTriage.
# Run once before first use: ./scripts/download-models.sh
# CI/Fly.io: add 'RUN ./scripts/download-models.sh' to Dockerfile.

set -euo pipefail

MODELS_DIR="$(cd "$(dirname "$0")/.." && pwd)/models"
MODEL_FILE="$MODELS_DIR/multilingual-e5-small/model.onnx"
MODEL_URL="https://huggingface.co/Xenova/multilingual-e5-small/resolve/main/onnx/model.onnx"

# sha256 — verify after first download: sha256sum models/multilingual-e5-small/model.onnx
EXPECTED_SHA256="FILL_AFTER_FIRST_DOWNLOAD"

echo "=== ApartmentTriage model download ==="
echo "Target: $MODEL_FILE"

mkdir -p "$(dirname "$MODEL_FILE")"

if [ -f "$MODEL_FILE" ]; then
    echo "Model already present. Skipping download."
else
    echo "Downloading multilingual-e5-small (ONNX)..."
    if command -v curl &>/dev/null; then
        curl -L --progress-bar -o "$MODEL_FILE" "$MODEL_URL"
    elif command -v wget &>/dev/null; then
        wget -q --show-progress -O "$MODEL_FILE" "$MODEL_URL"
    else
        echo "ERROR: curl or wget required." >&2
        exit 1
    fi
    echo "Download complete."
fi

# sha256 verification (skip if placeholder not yet filled)
if [ "$EXPECTED_SHA256" != "FILL_AFTER_FIRST_DOWNLOAD" ]; then
    echo "Verifying sha256..."
    if command -v sha256sum &>/dev/null; then
        ACTUAL=$(sha256sum "$MODEL_FILE" | awk '{print $1}')
    elif command -v shasum &>/dev/null; then
        ACTUAL=$(shasum -a 256 "$MODEL_FILE" | awk '{print $1}')
    else
        echo "WARNING: sha256sum/shasum not found, skipping checksum." >&2
        ACTUAL="$EXPECTED_SHA256"
    fi

    if [ "$ACTUAL" != "$EXPECTED_SHA256" ]; then
        echo "ERROR: sha256 mismatch!" >&2
        echo "  expected: $EXPECTED_SHA256" >&2
        echo "  actual:   $ACTUAL" >&2
        rm -f "$MODEL_FILE"
        exit 1
    fi
    echo "sha256 OK."
fi

echo "=== Done. Model path: $MODEL_FILE ==="
echo "Configure: Embeddings:ModelPath=$MODEL_FILE"
