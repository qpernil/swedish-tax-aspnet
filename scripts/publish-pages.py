#!/usr/bin/env python3
"""Publish the existing Blazor client as a static GitHub Pages artifact."""
import argparse
from pathlib import Path
import re
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--base-path", default="/swedish-tax-aspnet/")
    parser.add_argument("--output", type=Path, default=ROOT / "artifacts/pages")
    args = parser.parse_args()
    base = args.base_path
    if not re.fullmatch(r"/(?:[A-Za-z0-9_.-]+/)*", base) or any(part in (".", "..") for part in base.split("/")):
        raise SystemExit("The base path must be / or a path like /repository-name/.")
    output = args.output.resolve()
    if output.exists():
        raise SystemExit(f"Choose a fresh output directory (already exists): {output}")
    with tempfile.TemporaryDirectory(prefix="swedish-tax-pages-") as temp:
        publish = Path(temp) / "publish"
        subprocess.run([args.dotnet, "publish", str(ROOT / "src/SwedishTax.Web/SwedishTax.Web.Client"),
                        "-c", "Release", "-p:StaticSite=true", "-o", str(publish), "--nologo"], check=True)
        site = publish / "wwwroot"
        for name in ("index.html", "app.css", "SwedishTax.Web.Client.styles.css", "_framework/blazor.webassembly.js"):
            if not (site / name).is_file():
                raise SystemExit(f"Static publication is missing {name}")
        page = (site / "index.html").read_text()
        if page.count('<base href="/" />') != 1:
            raise SystemExit("Expected one base element in the standalone entry page")
        page = page.replace('<base href="/" />', f'<base href="{base}" />')
        (site / "index.html").write_text(page)
        # GitHub Pages serves this page for unknown paths; the Blazor router
        # displays its NotFound component with assets resolved at the site base.
        (site / "404.html").write_text(page)
        (site / ".nojekyll").touch()
        shutil.copytree(site, output)
    print(f"Static Pages artifact: {output} (base {base})")


if __name__ == "__main__":
    main()
