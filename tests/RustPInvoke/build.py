#!/usr/bin/env python3
"""Build the native reference and statically linked Blazor P/Invoke test harness."""

import argparse
import json
import os
from pathlib import Path
import shlex
import shutil
import subprocess
import sys

HERE = Path(__file__).resolve().parent
REPOSITORY = HERE.parents[1]


def run(command, **kwargs):
    print(shlex.join(map(str, command)), flush=True)
    return subprocess.run(list(map(str, command)), check=True, **kwargs)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rust-source", type=Path, default=REPOSITORY.parent / "swedish-tax")
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--output", type=Path, default=HERE / "bin/pinvoke-publish")
    args = parser.parse_args()
    source = args.rust_source.resolve()
    output = args.output.resolve()
    version = subprocess.check_output(["rustc", "--version"], text=True).strip()
    if not version.startswith("rustc 1.98.1 "):
        raise SystemExit("This test harness is verified with Rust 1.98.1. Select it with RUSTUP_TOOLCHAIN=1.98.1.")
    sdk = subprocess.check_output([args.dotnet, "--version"], text=True).strip()
    if sdk != "10.0.301":
        raise SystemExit("This test harness requires the repository's .NET SDK 10.0.301 and wasm-tools workload.")

    obj = HERE / "obj"
    obj.mkdir(exist_ok=True)
    run([sys.executable, HERE / "generate-bindings.py", "--header", source / "ios-ffi/include/SwedishTaxFFI.h", "--check"])
    shutil.copyfile(source / "ios-ffi/include/SwedishTaxFFI.h", obj / "SwedishTaxFFI.h")
    native_env = os.environ.copy()
    native_env.pop("RUSTFLAGS", None)
    native_env.pop("CARGO_ENCODED_RUSTFLAGS", None)
    native_env.pop("RUSTC_BOOTSTRAP", None)
    native_env["CARGO_TARGET_DIR"] = str(obj / "native")
    cargo = ["cargo", "build", "--manifest-path", source / "Cargo.toml", "-p", "swedish-tax-ios", "--release", "--locked"]
    run(cargo, env=native_env)

    reference = obj / "native-reference"
    run([os.environ.get("CC", "clang"), "-Wall", "-Wextra", "-Werror", HERE / "reference.c",
         "-I", source / "ios-ffi/include", obj / "native/release/libswedish_tax_ios.a", "-o", reference]
        + (["-ldl", "-lpthread", "-lm"] if sys.platform.startswith("linux") else []))
    cases = obj / "native-cases.json"
    with cases.open("w") as destination:
        run([reference], stdout=destination)
    with cases.open() as data:
        fixture = json.load(data)
    counts = {key: len(fixture[key]) for key in ("cases", "annualCases", "profileCases", "planCases")}
    if counts != {"cases": 1182, "annualCases": 336, "profileCases": 112, "planCases": 120}:
        raise SystemExit(f"Unexpected native fixture counts: {counts}")

    # Use the same pinned artifact and compiler settings as the application.
    run([sys.executable, REPOSITORY / "scripts/build-native-engine.py", "--source", source])
    run([args.dotnet, "publish", HERE, "-c", "Release", "-o", output, "--nologo",
         "-p:RustLibraryPath=" + str(REPOSITORY / "artifacts/native/libswedish_tax_ios.a")])

    revision = subprocess.check_output(["git", "-C", source, "rev-parse", "HEAD"], text=True).strip()
    dirty = bool(subprocess.check_output(["git", "-C", source, "status", "--porcelain"], text=True))
    (output / "build-info.json").write_text(json.dumps({
        "rustSourceCommit": revision, "rustSourceDirty": dirty,
        "rustCompiler": version, "dotnetSdk": sdk, "referenceCases": counts,
    }, indent=2) + "\n")
    print(f"Serve with: python3 -m http.server 5182 --bind 127.0.0.1 --directory {shlex.quote(str(output / 'wwwroot'))}")


if __name__ == "__main__":
    main()
