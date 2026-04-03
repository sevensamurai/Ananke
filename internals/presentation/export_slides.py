"""
Export all slide-*.html files from a release folder as 1080×1080 PNG files.

Requires:  pip install playwright
           playwright install chromium   (first time only)

Run:       python export_slides.py [folder]
           python export_slides.py release-3

  folder   subfolder containing the HTML files (default: release-1)

Output:    <folder>/slide-*.png  (next to the HTML files)
"""

import argparse
import glob
import os
from pathlib import Path

from playwright.sync_api import sync_playwright

WIDTH  = 1080
HEIGHT = 1080


def export(folder: str):
    html_files = sorted(glob.glob(os.path.join(folder, "slide-*.html")))
    if not html_files:
        print(f"No slide-*.html files found in '{folder}'.")
        return

    base_url = Path(folder).resolve().as_uri()

    with sync_playwright() as p:
        browser = p.chromium.launch()
        for html_path in html_files:
            filename = os.path.basename(html_path)
            file_url = f"{base_url}/{filename}"
            page = browser.new_page(viewport={"width": WIDTH, "height": HEIGHT})
            page.goto(file_url, wait_until="networkidle")
            out = os.path.join(folder, filename.replace(".html", ".png"))
            page.screenshot(path=out, clip={"x": 0, "y": 0, "width": WIDTH, "height": HEIGHT})
            print(f"  ✓  {out}")
            page.close()
        browser.close()


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Export slide HTMLs to 1080×1080 PNGs.")
    parser.add_argument("folder", nargs="?", default="release-1",
                        help="Subfolder containing slide-*.html files (default: release-1)")
    args = parser.parse_args()

    html_count = len(glob.glob(os.path.join(args.folder, "slide-*.html")))
    print(f"Exporting {html_count} slides from '{args.folder}'…")
    export(args.folder)
    print("Done.")
