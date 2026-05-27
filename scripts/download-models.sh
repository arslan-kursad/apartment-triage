#!/usr/bin/env bash
# Download model files for ApartmentTriage.
# Run once before first use: ./scripts/download-models.sh
# CI/Fly.io: add 'RUN ./scripts/download-models.sh' to Dockerfile.

set -euo pipefail

MODELS_DIR="$(cd "$(dirname "$0")/.." && pwd)/models"

# ── 1. multilingual-e5-small (ONNX, ~119 MB) ──────────────────────────────────
E5_FILE="$MODELS_DIR/multilingual-e5-small/model.onnx"
E5_URL="https://huggingface.co/Xenova/multilingual-e5-small/resolve/main/onnx/model.onnx"
E5_SHA256="4aa845c27760e06e9a686b9d8b5d440eae4b6612cd09e5b522b716d3941f77ff"

echo "=== [1/2] multilingual-e5-small ==="
mkdir -p "$(dirname "$E5_FILE")"

if [ -f "$E5_FILE" ]; then
    echo "Already present. Skipping."
else
    echo "Downloading..."
    if command -v curl &>/dev/null; then
        curl -L --progress-bar -o "$E5_FILE" "$E5_URL"
    elif command -v wget &>/dev/null; then
        wget -q --show-progress -O "$E5_FILE" "$E5_URL"
    else
        echo "ERROR: curl or wget required." >&2; exit 1
    fi
    echo "Download complete."
fi

if [ "$E5_SHA256" != "FILL_AFTER_FIRST_DOWNLOAD" ]; then
    echo "Verifying sha256..."
    if command -v sha256sum &>/dev/null; then
        ACTUAL=$(sha256sum "$E5_FILE" | awk '{print $1}')
    elif command -v shasum &>/dev/null; then
        ACTUAL=$(shasum -a 256 "$E5_FILE" | awk '{print $1}')
    else
        echo "WARNING: sha256sum/shasum not found, skipping." >&2
        ACTUAL="$E5_SHA256"
    fi
    if [ "$ACTUAL" != "$E5_SHA256" ]; then
        echo "ERROR: sha256 mismatch!" >&2
        echo "  expected: $E5_SHA256" >&2
        echo "  actual:   $ACTUAL" >&2
        rm -f "$E5_FILE"; exit 1
    fi
    echo "sha256 OK."
fi

echo "Path: $E5_FILE"
echo "Configure: Embeddings:ModelPath=$E5_FILE"

# ── 2. whisper-base (ggml, ~142 MB) ───────────────────────────────────────────
WHISPER_FILE="$MODELS_DIR/whisper/whisper-base.bin"
WHISPER_URL="https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin"
# sha256 — fill after first download: sha256sum models/whisper/whisper-base.bin
WHISPER_SHA256="60ed5bc3dd14eea856493d334349b405782ddcaf0028d4b5df4088345fba2efe"

echo ""
echo "=== [2/2] whisper-base (ggml) ==="
mkdir -p "$(dirname "$WHISPER_FILE")"

if [ -f "$WHISPER_FILE" ]; then
    echo "Already present. Skipping."
else
    echo "Downloading (~142 MB)..."
    if command -v curl &>/dev/null; then
        curl -L --progress-bar -o "$WHISPER_FILE" "$WHISPER_URL"
    elif command -v wget &>/dev/null; then
        wget -q --show-progress -O "$WHISPER_FILE" "$WHISPER_URL"
    else
        echo "ERROR: curl or wget required." >&2; exit 1
    fi
    echo "Download complete."
fi

if [ "$WHISPER_SHA256" != "FILL_AFTER_FIRST_DOWNLOAD" ]; then
    echo "Verifying sha256..."
    if command -v sha256sum &>/dev/null; then
        ACTUAL=$(sha256sum "$WHISPER_FILE" | awk '{print $1}')
    elif command -v shasum &>/dev/null; then
        ACTUAL=$(shasum -a 256 "$WHISPER_FILE" | awk '{print $1}')
    else
        echo "WARNING: sha256sum/shasum not found, skipping." >&2
        ACTUAL="$WHISPER_SHA256"
    fi
    if [ "$ACTUAL" != "$WHISPER_SHA256" ]; then
        echo "ERROR: sha256 mismatch!" >&2
        echo "  expected: $WHISPER_SHA256" >&2
        echo "  actual:   $ACTUAL" >&2
        rm -f "$WHISPER_FILE"; exit 1
    fi
    echo "sha256 OK."
fi

echo "Path: $WHISPER_FILE"
echo "Configure: Whisper:ModelPath=$WHISPER_FILE"
echo ""
echo "=== All models ready ==="
