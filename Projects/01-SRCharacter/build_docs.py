"""Generate markdown docs + csv from parsed capture data."""
import csv
import json
import os

OUT = os.path.dirname(os.path.abspath(__file__))
RAW = os.path.join(OUT, "raw")
res = json.load(open(os.path.join(RAW, "xml_resources.json"), encoding="utf-8"))
events = json.load(open(os.path.join(RAW, "xml_events_with_eid.json"), encoding="utf-8"))
passes = json.load(open(os.path.join(RAW, "xml_passes.json"), encoding="utf-8"))
detail = json.load(open(os.path.join(RAW, "pass_detail.json"), encoding="utf-8"))
states = json.load(open(os.path.join(RAW, "state_objects.json"), encoding="utf-8"))

tex, buf = res["textures"], res["buffers"]
srv, rtv, dsv, uav = res["srvs"], res["rtvs"], res["dsvs"], res["uavs"]
layouts = res["layouts"]
by_ci = {e["chunkIndex"]: e for e in events}
by_eid = {e.get("mcpEventId"): e for e in events if e.get("mcpEventId")}

DRAW_KINDS = {"DrawIndexed", "Draw", "DrawIndexedInstanced", "DrawInstanced",
              "DrawIndexedInstancedIndirect", "DrawInstancedIndirect", "DrawAuto"}

# manual role annotations derived from exported images + pass structure
ROLES = {
    0: "GPU skinning / vertex-stream pre-pass (VS+GS+SO), single probe draw",
    1: "Compute culling/skinning dispatch batch",
    2: "GPU skinning batches -> stream-out vertex buffers (49 draws)",
    3: "Bind main color RT 683 + depth 687 (state only)",
    4: "Compute pass on main framebuffer state",
    5: "Clear shadow atlas (3072x3072 R16 depth)",
    6: "Directional shadow map (cascade) depth-only",
    7: "Spot/point shadow map clear + depth-only",
    8: "Opaque GBuffer pass: albedo RGBA8 + normal RGBA16F + mask R8, depth/stencil D32S8; characters write stencil IDs",
    9: "Half-res depth copy + fullscreen draws",
    10: "Half-res compute effects (6 dispatches)",
    11: "Half-res compute effects (state only)",
    12: "128x128 probe render",
    13: "128x128 compute",
    14: "128x128 state",
    15: "128x128 compute",
    16: "CSM shadow atlas tile 0 (512x512) depth-only",
    17: "CSM shadow atlas tile 1",
    18: "CSM shadow atlas tile 2",
    19: "CSM shadow atlas tile 3",
    20: "CSM shadow atlas tile 4",
    21: "CSM shadow atlas tile 5",
    22: "CSM shadow atlas tile 6",
    23: "CSM shadow atlas tile 7",
    24: "CSM shadow atlas tile 8",
    25: "CSM shadow atlas tile 9",
    26: "CSM shadow atlas tile 10",
    27: "Screen-space shadow/occlusion mask (R8) for characters/objects",
    28: "Compute dispatch batch (main view)",
    29: "Fog / atmosphere pass into R11G11B10",
    30: "Fullscreen composite into RGBA8 (samples depth, GBuffer, masks)",
    31: "Compute batch on composite target",
    32: "Half-res 864x540 dual-RGBA16F effect",
    33: "1728x1080 RGBA16F pass",
    34: "1728x1080 RGBA16F pass",
    35: "Deferred lighting / material shading into HDR R11G11B10: stencil-masked fullscreen material passes + forward geometry overlays (characters shaded here)",
    36: "Post-lighting MRT pass: R16G16 + R8 mask (character silhouette / effect mask consumed by bloom)",
    37: "Bloom extract (full-res 3072x1920 -> tex5300)",
    38: "Bloom downsample 1536x960",
    39: "Bloom downsample 768x480",
    40: "Bloom downsample 320x200",
    41: "Bloom downsample 144x90",
    42: "Bloom downsample 64x40",
    43: "Bloom downsample 32x20",
    44: "Bloom atlas clear (465x200)",
    45: "Bloom upsample (mip 320x200 into atlas)",
    46: "Bloom upsample (mip 144x90)",
    47: "Bloom upsample (mip 64x40)",
    48: "Bloom upsample (mip 32x20)",
    49: "Bloom atlas clear (second level)",
    50: "Bloom upsample (second level)",
    51: "Bloom upsample (second level)",
    52: "Bloom upsample (second level)",
    53: "Bloom upsample (second level)",
    54: "Bloom final combine 320x200",
    55: "Tonemap + UI composition into RT 683 (43 draws)",
    56: "Present blit to swapchain texture 678",
}


def tinfo(tid):
    t = tex.get(str(tid))
    return t


def tf(tid):
    t = tinfo(tid)
    if not t:
        return f"tex{tid}"
    return f"tex{tid} {t['w']}x{t['h']} {t['fmt'].replace('DXGI_FORMAT_','')}"


def bf(bid):
    b = buf.get(str(bid))
    if not b:
        return f"buf{bid}"
    return f"buf{bid} {b['byteWidth']}B"


def stage_union(prog):
    """Union of SRV textures and CB buffers across stages of one program group."""
    srv_tex = {}
    cbs = {}
    for stage, sv in prog["stages"].items():
        for s in sv["srvs"]:
            if s["tex"]:
                srv_tex.setdefault(s["tex"]["id"], set()).add(f"{stage}{s['slot']}")
        for c in sv["cbs"]:
            cbs.setdefault(c["buffer"]["id"], set()).add(f"{stage}{c['slot']}:{c['buffer'].get('bytes')}B")
    return srv_tex, cbs


def tex_consumers_srv(prog):
    out = {}
    for stage, sv in prog["stages"].items():
        for s in sv["srvs"]:
            if s["tex"]:
                out.setdefault(s["tex"]["id"], set()).add(f"{stage}{s['slot']}")
    return out


def tex_writers(p):
    """Textures written by a pass (RTV/DSV/UAV/copy dst)."""
    w = {}
    for c in p["rtvColor"]:
        if c["tex"]:
            w.setdefault(c["tex"]["id"], "RTV")
    if p["depth"] and p["depth"]["tex"]:
        w.setdefault(p["depth"]["tex"]["id"], "DSV")
    for prog in p["programs"]:
        for u in prog["uavs"]:
            if u["tex"]:
                w.setdefault(u["tex"]["id"], "UAV")
    for c in p["copies"]:
        a = c.get("args") or {}
        if "dst" in a:
            w.setdefault(a["dst"], "CopyDst")
    return w


def tex_reads(p):
    r = {}
    for prog in p["programs"]:
        for stage, sv in prog["stages"].items():
            for s in sv["srvs"]:
                if s["tex"]:
                    r.setdefault(s["tex"]["id"], set()).add(f"{stage}{s['slot']}")
        for c in sv["cbs"]:
            pass
    for c in p["copies"]:
        a = c.get("args") or {}
        if "src" in a:
            r.setdefault(a["src"], set()).add("copy")
    return r


# ---------------- CSV of every draw ----------------
def write_csv():
    path = os.path.join(RAW, "draws.csv")
    with open(path, "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(["pass", "chunkIndex", "mcpEventId", "kind", "indexCount",
                    "vs", "hs", "ds", "gs", "ps", "layout", "vb0", "ib",
                    "rtv0", "rtv1", "rtv2", "dsv", "blend", "dss", "rast",
                    "ps_srv_tex", "vs_cb", "ps_cb"])
        for p in detail:
            for prog in p["programs"]:
                for ci in prog["draws"]:
                    e = by_ci.get(ci)
                    if not e:
                        continue
                    st = e["state"]
                    args = e.get("args") or {}
                    n = args.get("indexCount") or args.get("vertexCount") or 0
                    psv = prog["stages"].get("ps", {}).get("srvs", [])
                    ptxt = " ".join(str(s["tex"]["id"]) if s["tex"] else f"view{s['view']}" for s in psv)
                    vcb = " ".join(f"cb{c['slot']}={c['buffer'].get('bytes')}" for c in prog["stages"].get("vs", {}).get("cbs", []))
                    pcb = " ".join(f"cb{c['slot']}={c['buffer'].get('bytes')}" for c in prog["stages"].get("ps", {}).get("cbs", []))
                    w.writerow([p["index"], ci, e.get("mcpEventId"), e["kind"], n,
                                prog["vs"], prog["hs"], prog["ds"], prog["gs"], prog["ps"],
                                prog["layout"], (prog["vb"][0]["buf"] if prog["vb"] else ""), prog["ib"],
                                *( [c["tex"]["id"] if c["tex"] else "" for c in p["rtvColor"]] + ["", "", ""])[:3],
                                p["depth"]["tex"]["id"] if p["depth"] and p["depth"]["tex"] else "",
                                prog["blend"], prog["depthStencil"], prog["rasterizer"], ptxt, vcb, pcb])
    return path


# ---------------- pass overview markdown ----------------
def md_overview():
    lines = ["# StarRail frame 13063 - pass/event overview", ""]
    lines.append("Capture: `E:\\FileRecv\\StarRail_2024.12.09_01.39_frame13063.rdc` (D3D11, RTX 3050 Laptop, 622 events, 581 draws).")
    lines.append("Event ids below are RenderDoc event ids; `chunkIndex` is the index in the XML chunk stream (used for ordering).")
    lines.append("")
    lines.append("| pass | events (eid) | draws | dispatches | indices | viewport | color RT | depth | inferred role |")
    lines.append("|---|---|---|---|---|---|---|---|---|")
    for p, d in zip(passes, detail):
        rs = " + ".join(f"tex{c['tex']['id']}" for c in d["rtvColor"] if c["tex"]) or "-"
        ds = f"tex{d['depth']['tex']['id']}" if d["depth"] and d["depth"]["tex"] else "-"
        vp = d["viewport"]
        vps = f"{int(vp['w'])}x{int(vp['h'])}@{int(vp['x'])},{int(vp['y'])}" if vp else "-"
        mcp = [e.get("mcpEventId") for e in events if p["startEvent"] <= e["chunkIndex"] <= p["endEvent"] and e.get("mcpEventId")]
        evs = f"{min(mcp)}-{max(mcp)}" if mcp else f"chunk {p['startEvent']}-{p['endEvent']}"
        lines.append(f"| P{p['index']:02d} | {evs} | {d['drawCount']} | {d['dispatchCount']} | {d['totalIndices']} | {vps} | {rs} | {ds} | {ROLES.get(p['index'],'')} |")
    return "\n".join(lines)


# ---------------- pass bindings markdown ----------------
def md_bindings():
    lines = ["# Per-pass bindings (RTV / SRV / CB / states)", "", "Note: for compute passes (P04/P28/P31) the listed RTV/DSV is residual Output-Merger state, not a compute output.", ""]
    for p, d in zip(passes, detail):
        lines.append(f"## P{p['index']:02d} - {ROLES.get(p['index'],'')}")
        lines.append("")
        mcp = [e.get("mcpEventId") for e in events if p["startEvent"] <= e["chunkIndex"] <= p["endEvent"] and e.get("mcpEventId")]
        evs = f"{min(mcp)}-{max(mcp)}" if mcp else "-"
        lines.append(f"- draw eids: {evs}; draws={d['drawCount']}, dispatches={d['dispatchCount']}, indices={d['totalIndices']}")
        vp = d["viewport"]
        if vp:
            lines.append(f"- viewport: x={int(vp['x'])} y={int(vp['y'])} w={int(vp['w'])} h={int(vp['h'])}")
        cols = [f"{tf(c['tex']['id'])} (view {c['view']})" for c in d["rtvColor"] if c["tex"]]
        if d["depth"] and d["depth"]["tex"]:
            depthextra = ""
            dsset = {prog["depthStencil"] for prog in d["programs"]}
            st = [states["depthStencil"].get(str(s)) for s in dsset if states["depthStencil"].get(str(s))]
            if st and all(x and x.get("frontFace", {}).get("pass") == "D3D11_STENCIL_OP_REPLACE" for x in st):
                depthextra = " (stencil REPLACE: material IDs)"
            lines.append(f"- depth: {tf(d['depth']['tex']['id'])} (DSV {d['depth']['view']}){depthextra}")
        else:
            lines.append("- depth: none")
        if cols:
            lines.append("- color RTs: " + "; ".join(cols))
        for c in d["clears"]:
            lines.append(f"- clear: {c['kind']} {c['args']}")
        for c in d["copies"]:
            lines.append(f"- copy: {c['kind']} {c['args']}")
        if d["dispatches"]:
            lines.append(f"- dispatch chunkIndex: {d['dispatches']}")

        # union of SRVs / CBs at pass level
        all_srv = {}
        all_cb = {}
        for prog in d["programs"]:
            for tid, slots in tex_consumers_srv(prog).items():
                all_srv.setdefault(tid, set()).update(slots)
            _, cbs = stage_union(prog)
            for bid, slots in cbs.items():
                all_cb.setdefault(bid, set()).update(slots)
        if all_srv:
            lines.append("- SRV textures (union): " + "; ".join(
                f"{tf(tid)} [{','.join(sorted(s))}]" for tid, s in sorted(all_srv.items(), key=lambda x: -len(x[1]))[:20]))
        if all_cb:
            lines.append("- CB buffers (union): " + "; ".join(
                f"{bf(bid)} [{','.join(sorted(s))}]" for bid, s in sorted(all_cb.items(), key=lambda x: -len(x[1]))[:20]))
        lines.append("")
        if d["programs"]:
            lines.append("| #draws | indices | VS | PS | GS | blend | DSS | RS | layout | VB0 | IB | SRV (ps) | CB (vs) | CB (ps) |")
            lines.append("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|")
            for prog in d["programs"]:
                ps = prog["stages"].get("ps")
                psv = " ".join(f"s{s['slot']}:{s['tex']['id']}" if s["tex"] else f"s{s['slot']}:v{s['view']}" for s in (ps["srvs"] if ps else []))
                vs = prog["stages"].get("vs")
                vcb = " ".join(f"cb{c['slot']}={c['buffer'].get('bytes')}" for c in (vs["cbs"] if vs else []))
                pcb = " ".join(f"cb{c['slot']}={c['buffer'].get('bytes')}" for c in (ps["cbs"] if ps else []))
                lines.append(f"| {prog['drawCount']} | {prog['indices']} | {prog['vs']} | {prog['ps']} | {prog['gs'] or '-'} | {prog['blend']} | {prog['depthStencil']} | {prog['rasterizer']} | {prog['layout']} | {prog['vb'][0]['buf'] if prog['vb'] else '-'} | {prog['ib']} | {psv} | {vcb} | {pcb} |")
        lines.append("")
    return "\n".join(lines)


# ---------------- dependency markdown ----------------
def md_deps():
    writers = {}
    readers = {}
    for p in detail:
        for tid, kind in tex_writers(p).items():
            writers.setdefault(tid, []).append((p["index"], kind))
        for tid, slots in tex_reads(p).items():
            readers.setdefault(tid, []).append((p["index"], ",".join(sorted(slots))[:60]))
    edges = {}
    for tid, ws in writers.items():
        for rs in readers.get(tid, []):
            for wpi, kind in ws:
                if wpi < rs[0] and kind in ("RTV", "DSV", "UAV", "CopyDst"):
                    edges.setdefault((wpi, rs[0]), set()).add(tid)
    lines = ["# Resource dependency graph (producer -> consumer)", ""]
    lines.append("Generated from RTV/DSV/UAV writes and SRV/copy reads per pass.")
    lines.append("")
    lines.append("```mermaid")
    lines.append("flowchart TD")
    for (a, b), tids in sorted(edges.items()):
        label = ",".join(f"{tid}" for tid in sorted(tids)[:6])
        lines.append(f"  P{a:02d} -->|{label}| P{b:02d}")
    lines.append("```")
    lines.append("")
    lines.append("## Texture producer/consumer table (key RTs)")
    lines.append("")
    lines.append("| texture | format | written by | read by (pass:slots) |")
    lines.append("|---|---|---|---|")
    key = [9503, 9507, 9511, 9515, 9580, 9596, 15780, 5279, 9621, 5288, 683, 678,
           14816, 18188, 17638, 9529, 3403, 5267, 514, 9499]
    for tid in key:
        t = tinfo(tid)
        if not t:
            continue
        ws = ", ".join(f"P{a}({k})" for a, k in writers.get(tid, []))
        rs = ", ".join(f"P{a}({s})" for a, s in readers.get(tid, []))
        lines.append(f"| tex{tid} | {t['fmt'].replace('DXGI_FORMAT_','')} {t['w']}x{t['h']} | {ws or '-'} | {rs or '-'} |")
    lines.append("")
    lines.append("All textures and their usage are in `raw/pass_detail.json` (per pass) and `raw/xml_resources.json`.")
    return "\n".join(lines)


def write(path, text):
    with open(path, "w", encoding="utf-8") as f:
        f.write(text)
    print("wrote", path)


write(os.path.join(RAW, "draws.csv"), "") if False else write_csv()
write(os.path.join(OUT, "01-frame-pass-overview.md"), md_overview())
write(os.path.join(OUT, "02-pass-bindings.md"), md_bindings())
write(os.path.join(OUT, "03-resource-dependencies.md"), md_deps())
print("done")
