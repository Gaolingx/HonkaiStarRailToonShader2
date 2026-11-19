#!/usr/bin/env python3
"""Extract per-draw stencil references from a RenderDoc XML export.

RenderDoc's MCP bindings only expose reflection names; the XML chunk stream still
carries `OMSetDepthStencilState` with the `StencilRef` value, which is what pinned
down the HSR stencil encoding used by this module.

Usage:
    renderdoccmd convert -c xml -o frame.xml capture.rdc
    python extract_stencil_refs.py frame.xml draws.csv

Output: raw/draw_stencil_refs.csv  (chunkIndex, dssObject, stencilRef)
Join on chunkIndex with ../../01-SRCharacter/raw/draws.csv.
"""
import csv
import re
import sys
from pathlib import Path

CHUNK_RE = re.compile(r'<chunk id="(\d+)" chunkIndex="(\d+)" name="([^"]+)"')
STENCIL_RE = re.compile(r'name="StencilRef"[^>]*>(\d+)<')
DSS_RE = re.compile(r'name="pDepthStencilState"[^>]*>(\d+)<')


def main(xml_path: str, out_path: str) -> None:
    text = Path(xml_path).read_text(encoding="utf-8", errors="ignore")
    state = {}
    rows = []

    # Chunks are newline-tab-tab separated in RenderDoc's XML export.
    for part in text.split("\n\t\t<chunk ")[1:]:
        match = CHUNK_RE.match(part)
        if not match:
            continue
        _, chunk_index, name = int(match.group(1)), int(match.group(2)), match.group(3)

        if name.endswith("OMSetDepthStencilState"):
            stencil = STENCIL_RE.search(part)
            dss = DSS_RE.search(part)
            if stencil:
                state["ref"] = int(stencil.group(1))
            if dss:
                state["dss"] = int(dss.group(1))
        elif "Draw" in name:
            rows.append((chunk_index, state.get("dss", 0), state.get("ref", 0)))

    out = Path(out_path)
    out.parent.mkdir(parents=True, exist_ok=True)
    with out.open("w", newline="", encoding="utf-8") as handle:
        writer = csv.writer(handle)
        writer.writerow(["chunkIndex", "dssObject", "stencilRef"])
        writer.writerows(rows)

    print(f"wrote {len(rows)} draws to {out}")


if __name__ == "__main__":
    if len(sys.argv) != 3:
        print(__doc__)
        raise SystemExit(2)
    main(sys.argv[1], sys.argv[2])