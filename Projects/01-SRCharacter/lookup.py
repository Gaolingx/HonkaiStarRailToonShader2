"""Look up full state for given MCP event ids (draws)."""
import json
import os
import sys

OUT = os.path.dirname(os.path.abspath(__file__))
RAW = os.path.join(OUT, "raw")
res = json.load(open(os.path.join(RAW, "xml_resources.json"), encoding="utf-8"))
events = json.load(open(os.path.join(RAW, "xml_events_with_eid.json"), encoding="utf-8"))
passes = json.load(open(os.path.join(RAW, "xml_passes.json"), encoding="utf-8"))
detail = json.load(open(os.path.join(RAW, "pass_detail.json"), encoding="utf-8"))
states = json.load(open(os.path.join(RAW, "state_objects.json"), encoding="utf-8"))

tex, buf, srv, rtv, dsv, uav = (res["textures"], res["buffers"], res["srvs"], res["rtvs"],
                                res["dsvs"], res["uavs"])
layouts = res["layouts"]

by_eid = {e.get("mcpEventId"): e for e in events if e.get("mcpEventId")}
pass_of = {}
for p in passes:
    for c in p["draws"] + p["dispatches"]:
        pass_of[c] = p["index"]


def tname(tid):
    t = tex.get(str(tid))
    if not t:
        return f"tex{tid}"
    return f"tex{tid}({t['w']}x{t['h']} {t['fmt']})"


def describe(eid):
    e = by_eid.get(eid)
    if not e:
        print(f"eid {eid}: not a mapped draw")
        return
    st = e["state"]
    pi = pass_of.get(e["chunkIndex"])
    print(f"\n=== eid {eid}  pass P{pi}  kind={e['kind']}  args={e.get('args')}")
    for stage in ("vs", "hs", "ds", "gs", "ps", "cs"):
        sid = st["shaders"].get(stage)
        if not sid:
            continue
        print(f"  {stage.upper()} shader {sid}")
        srs = st["srv"].get(stage, {})
        for slot, vid in sorted(srs.items(), key=lambda x: int(x[0])):
            v = srv.get(str(vid), {})
            print(f"     SRV s{slot} <- view{vid} {tname(v.get('res'))}")
        cbs = st["cb"].get(stage, {})
        for slot, bid in sorted(cbs.items(), key=lambda x: int(x[0])):
            b = buf.get(str(bid), {})
            print(f"     CB cb{slot} <- buf{bid} {b.get('byteWidth')}B {b.get('bind')}")
    lay = layouts.get(str(st.get("layout") or 0))
    if lay:
        print("  IL:", ", ".join(f"{x['sem']}{x['idx']}@{x['slot']}:{x['fmt']}" for x in lay))
    print("  VB:", [(v["slot"], v["buf"], v["stride"]) for v in st.get("vb", [])], "IB:", st.get("ib"))
    print("  RTV:", [tname(rtv.get(str(v), {}).get("res")) for v in st["rtv"] if v],
          "DSV:", tname(dsv.get(str(st["dsv"]), {}).get("res")) if st["dsv"] else None)
    for kind, key in (("blend", "blend"), ("depthStencil", "depthstencil"), ("rasterizer", "rasterizer")):
        sid = st.get(key)
        print(f"  {kind} state {sid}:", json.dumps(states[kind].get(str(sid))))


if __name__ == "__main__":
    for a in sys.argv[1:]:
        describe(int(a))
