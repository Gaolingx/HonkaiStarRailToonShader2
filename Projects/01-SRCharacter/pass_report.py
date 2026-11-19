"""Build detailed per-pass/per-program binding report from parsed XML data."""
import json
import os

OUT = os.path.dirname(os.path.abspath(__file__))
RAW = os.path.join(OUT, "raw")
res = json.load(open(os.path.join(RAW, "xml_resources.json"), encoding="utf-8"))
events = json.load(open(os.path.join(RAW, "xml_events.json"), encoding="utf-8"))
passes = json.load(open(os.path.join(RAW, "xml_passes.json"), encoding="utf-8"))

tex = res["textures"]
buf = res["buffers"]
srv = res["srvs"]
rtv = res["rtvs"]
dsv = res["dsvs"]
uav = res["uavs"]
shaders = res["shaders"]
layouts = res["layouts"]

ev_by_ci = {e["chunkIndex"]: e for e in events}


def tex_res(view_id, viewmap):
    v = viewmap.get(str(view_id))
    return v.get("res") if v else None


def tinfo(tid):
    t = tex.get(str(tid))
    if not t:
        return None
    return {"id": tid, "w": t["w"], "h": t["h"], "fmt": t["fmt"], "mips": t["mips"],
            "array": t["array"], "bind": t["bind"]}


def bdesc(bid):
    b = buf.get(str(bid))
    if not b:
        return {"id": bid}
    return {"id": bid, "bytes": b["byteWidth"], "bind": b["bind"],
            "usage": b["usage"], "stride": b["stride"]}


def draw_amount(ev):
    a = ev.get("args") or {}
    return a.get("indexCount") or a.get("vertexCount") or 0


detail = []
for p in passes:
    pv = p["rtvViews"]
    color = []
    for v in pv:
        if not v:
            continue
        r = rtv.get(str(v), {})
        t = tinfo(r.get("res"))
        color.append({"view": v, "fmt": r.get("fmt"), "tex": t})
    dv = p["dsvView"]
    dres = dsv.get(str(dv), {})
    depth = None
    if dv:
        depth = {"view": dv, "fmt": dres.get("fmt"), "tex": tinfo(dres.get("res"))}

    progs = []
    for prog in p["programs"]:
        draws = prog["draws"]
        ev0 = ev_by_ci[draws[0]]
        st = ev0["state"]
        stages = {}
        for stage in ("vs", "hs", "ds", "gs", "ps", "cs"):
            sid = st["shaders"].get(stage) or 0
            if not sid:
                continue
            shadows = dict(st["srv"].get(stage, {}))
            cbinds = dict(st["cb"].get(stage, {}))
            srvs = []
            for slot, vid in sorted(shadows.items(), key=lambda x: int(x[0])):
                vv = srv.get(str(vid), {})
                tr = tinfo(vv.get("res"))
                srvs.append({"slot": int(slot), "view": vid, "fmt": vv.get("fmt"),
                             "tex": tr})
            cbs = []
            for slot, bid in sorted(cbinds.items(), key=lambda x: int(x[0])):
                cbs.append({"slot": int(slot), "buffer": bdesc(int(bid))})
            stages[stage] = {"shader": sid, "srvs": srvs, "cbs": cbs,
                             "samplers": len(st["samplers"].get(stage, {}))}
        uavs = []
        for slot, vid in sorted(st.get("uav", {}).items(), key=lambda x: int(x[0])):
            uu = uav.get(str(vid), {})
            uavs.append({"slot": int(slot), "view": vid, "tex": tinfo(uu.get("res"))})
        so = []
        for bid in st.get("so", []):
            if bid:
                so.append(bdesc(bid))
        lay = layouts.get(str(st.get("layout") or 0))
        total = sum(draw_amount(ev_by_ci[ci]) for ci in draws)
        progs.append({
            "vs": prog["vs"], "ps": prog["ps"], "gs": prog["gs"],
            "hs": prog["hs"], "ds": prog["ds"],
            "draws": draws, "drawCount": len(draws), "indices": total,
            "layout": st.get("layout") or 0,
            "layoutElements": lay,
            "blend": prog["blend"], "depthStencil": prog["depthstencil"],
            "rasterizer": prog["rasterizer"],
            "stages": stages, "uavs": uavs, "so": so,
            "vb": st.get("vb"),
            "ib": st.get("ib"),
        })
    detail.append({
        "index": p["index"], "startEvent": p["startEvent"], "endEvent": p["endEvent"],
        "rtvColor": color, "depth": depth, "viewport": p["viewport"],
        "drawCount": len(p["draws"]),
        "dispatchCount": len(p["dispatches"]),
        "totalIndices": p["totalIndices"],
        "clears": [{"kind": c["kind"], "args": c.get("args")} for c in p["clears"]],
        "copies": [{"kind": c["kind"], "args": c.get("args")} for c in p["copies"]],
        "dispatches": p["dispatches"],
        "programs": progs,
    })

with open(os.path.join(RAW, "pass_detail.json"), "w", encoding="utf-8") as f:
    json.dump(detail, f, indent=1)


def fmt_tex(t):
    if not t:
        return "-"
    return f"tex{t['id']} {t['w']}x{t['h']} {t['fmt']}"


def fmt_srv(s):
    t = s["tex"]
    if t:
        return f"s{s['slot']}:{fmt_tex(t)}"
    return f"s{s['slot']}:view{s['view']}"


def print_pass(p, maxp=12):
    rs = " + ".join(fmt_tex(c["tex"]) for c in p["rtvColor"]) or "(no color)"
    ds = fmt_tex(p["depth"]["tex"]) if p["depth"] else "(no depth)"
    vp = p["viewport"]
    vps = f"{int(vp['w'])}x{int(vp['h'])}@{int(vp['x'])},{int(vp['y'])}" if vp else "-"
    print(f"\n===== P{p['index']:02d} ev{p['startEvent']}-{p['endEvent']} draws={p['drawCount']} disp={p['dispatchCount']} vp={vps}")
    print(f"  color: {rs}")
    print(f"  depth: {ds}")
    for c in p["clears"]:
        print(f"  clear: {c['kind']} {c['args']}")
    for prog in p["programs"][:maxp]:
        s = prog["stages"]
        sh = "/".join(f"{k.upper()}{v['shader']}" for k, v in s.items())
        print(f"  [{prog['drawCount']} draws, {prog['indices']} idx] {sh} blend={prog['blend']} ds={prog['depthStencil']} rs={prog['rasterizer']} layout={prog['layout']}")
        for stage, sv in s.items():
            srs = " ".join(fmt_srv(x) for x in sv["srvs"][:10])
            cbs = " ".join(f"cb{c['slot']}=buf{c['buffer'].get('bytes')}B" for c in sv["cbs"])
            print(f"     {stage}: SRV[{len(sv['srvs'])}] {srs}")
            if cbs:
                print(f"     {stage}: CB {cbs}")
        if prog["uavs"]:
            print(f"     UAV: {[u['tex']['id'] if u['tex'] else u['view'] for u in prog['uavs']]}")
        if prog["so"]:
            print(f"     SO: {[b['id'] for b in prog['so']]}")
        if prog["layoutElements"]:
            print(f"     IL: " + ", ".join(f"{e['sem']}{e['idx']}@{e['slot']}:{e['fmt']}" for e in prog["layoutElements"][:14]))


if __name__ == "__main__":
    import sys
    if len(sys.argv) > 1:
        want = set(int(x) for x in sys.argv[1].split(","))
        for p in detail:
            if p["index"] in want:
                print_pass(p, maxp=99)
    else:
        for p in detail:
            print_pass(p)
