#!/usr/bin/env python3
"""Verify the provider and build its shared C interface for Blazor and native tests."""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]

def run(args, **kwargs):
    return subprocess.run([str(a) for a in args], check=True, **kwargs)

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=ROOT.parent / "swedish-tax")
    args = parser.parse_args()
    source = args.source.resolve()
    pin = json.loads((ROOT / "native-engine.json").read_text())
    rustc = subprocess.check_output(["rustc", "--version"], text=True).strip()
    if not rustc.startswith("rustc " + pin["rust_toolchain"] + " "):
        raise SystemExit("Select RUSTUP_TOOLCHAIN=" + pin["rust_toolchain"])
    revision = subprocess.check_output(["git", "-C", source, "rev-parse", "HEAD"], text=True).strip()
    expected = subprocess.check_output(["git", "-C", source, "rev-parse", pin["revision"]], text=True).strip()
    if revision != expected or subprocess.check_output(["git", "-C", source, "status", "--porcelain"], text=True).strip():
        raise SystemExit("Rust source must be clean at the revision in native-engine.json")
    run([sys.executable, ROOT / "tests/RustPInvoke/generate-bindings.py", "--header", source / "ios-ffi/include/SwedishTaxFFI.h", "--check"])
    fixtures = source / "tests/fixtures"
    checked = ROOT / "tests/fixtures"
    if {p.name: p.read_bytes() for p in fixtures.glob("*.json")} != {p.name: p.read_bytes() for p in checked.glob("*.json")}:
        raise SystemExit("Shared Rust fixture drift; review and synchronize consumer tests/fixtures from provider tests/fixtures")
    if pin["target"] != "wasm32-unknown-emscripten":
        raise SystemExit("cargo xtask wasm requires the wasm32-unknown-emscripten target")
    artifacts = ROOT / "artifacts"
    native = artifacts / "native"
    native.mkdir(parents=True, exist_ok=True)
    env = os.environ.copy()
    for key in ("RUSTFLAGS", "CARGO_ENCODED_RUSTFLAGS", "RUSTC_BOOTSTRAP"):
        env.pop(key, None)
    env["CARGO_TARGET_DIR"] = str(artifacts / "host")
    run(["cargo", "rustc", "--manifest-path", source / "Cargo.toml", "-p", "swedish-tax-ios", "--release", "--locked", "--crate-type", "cdylib"], env=env)
    suffix = "dylib" if sys.platform == "darwin" else "so"
    shutil.copyfile(artifacts / f"host/release/libswedish_tax_ios.{suffix}", native / f"libswedish_tax_ios.{suffix}")
    env["CARGO_TARGET_DIR"] = str(artifacts / "xtask")
    run(["cargo", "xtask", "wasm", "--release", "--target-dir", artifacts / "rust",
         "--output", native, "--rustflags", pin["rustflags"]], cwd=source, env=env)
    if subprocess.check_output(["git", "-C", source, "rev-parse", "HEAD"], text=True).strip() != revision \
            or subprocess.check_output(["git", "-C", source, "status", "--porcelain"], text=True).strip():
        raise SystemExit("Rust source changed during the build; review and commit the provider before rebuilding")
    (native / "build-info.json").write_text(json.dumps({**pin, "revision": revision, "rustc": rustc}, indent=2) + "\n")
    print(f"Built current C ABI at {revision}")

if __name__ == "__main__":
    main()
