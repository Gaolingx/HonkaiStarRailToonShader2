"""Align XML action order with MCP event IDs (per kind) and emit raw/eid_map.json."""
import json
import os

OUT = os.path.dirname(os.path.abspath(__file__))
RAW = os.path.join(OUT, "raw")
TMP = r"C:\Users\Gaolingxiang\.qoder-cn\tmp\E--GitHub-Glx-CustomURP\tool-outputs\session-f14f4cea-591c-467b-a5f6-3ba4067610d4"
draws_file = os.path.join(TMP, "mcp_call_call_a904cbe3659f48a7be706d5c.txt")
events_file = os.path.join(TMP, "mcp_call_call_1675f88ce7894dff9d797040.txt")


def load_json_payload(path):
    text = open(path, encoding="utf-8").read()
    start = text.find("{")
    if start < 0 or (text.find("[", 0, start) >= 0 and text.find("[", 0, start) < start):
        start = text.find("[")
    return json.loads(text[start:text.rfind("}") + 1 if text.rfind("}") > text.rfind("]") else text.rfind("]") + 1])


payload = load_json_payload(draws_file)
mcp_draws = payload["draws"] if isinstance(payload, dict) else payload
ev_payload = load_json_payload(events_file)
mcp_events = ev_payload["events"] if isinstance(ev_payload, dict) else ev_payload

xml_events = json.load(open(os.path.join(RAW, "xml_events.json"), encoding="utf-8"))

DRAW_KINDS = {"DrawIndexed", "Draw", "DrawIndexedInstanced", "DrawInstanced",
              "DrawIndexedInstancedIndirect", "DrawInstancedIndirect", "DrawAuto"}

# group MCP event ids per normalized kind
mcp_by_kind = {}
for e in mcp_events:
    k = e.get("flags", "")
    mcp_by_kind.setdefault(k, []).append(e["eventId"])

mcp_draw_ids = [d["eventId"] for d in mcp_draws]
idx = {k: 0 for k in mcp_by_kind}

for ev in xml_events:
    kind = ev["kind"]
    if kind in DRAW_KINDS:
        if idx.setdefault("__draws", 0) < len(mcp_draw_ids):
            ev["mcpEventId"] = mcp_draw_ids[idx["__draws"]]
        idx["__draws"] = idx.get("__draws", 0) + 1
    else:
        lst = mcp_by_kind.get(kind)
        if lst and idx.get(kind, 0) < len(lst):
            ev["mcpEventId"] = lst[idx[kind]]
            idx[kind] = idx.get(kind, 0) + 1

unmapped = [e for e in xml_events if "mcpEventId" not in e]
print("xml actions:", len(xml_events), "unmapped:", len(unmapped),
      sorted({e["kind"] for e in unmapped}))

with open(os.path.join(RAW, "xml_events_with_eid.json"), "w", encoding="utf-8") as f:
    json.dump(xml_events, f, indent=1)

# per-pass ordinal ranges
passes = json.load(open(os.path.join(RAW, "xml_passes.json"), encoding="utf-8"))
draw_order = [e for e in xml_events if e["kind"] in DRAW_KINDS]
ordinal = {}
for i, e in enumerate(draw_order):
    ordinal[e["chunkIndex"]] = i

rows = []
for p in passes:
    ds = [ordinal[c] for c in p["draws"] if c in ordinal]
    rows.append({
        "pass": p["index"], "startEvent": p["startEvent"], "endEvent": p["endEvent"],
        "firstDrawOrdinal": ds[0] if ds else None,
        "lastDrawOrdinal": ds[-1] if ds else None,
        "firstDrawEid": draw_order[ds[0]]["mcpEventId"] if ds else None,
        "lastDrawEid": draw_order[ds[-1]]["mcpEventId"] if ds else None,
        "draws": len(ds),
    })
with open(os.path.join(RAW, "pass_mcp_events.json"), "w", encoding="utf-8") as f:
    json.dump(rows, f, indent=1)
for r in rows:
    print(f"P{r['pass']:02d} draws={r['draws']:3d} ord={r['firstDrawOrdinal']}..{r['lastDrawOrdinal']} eid={r['firstDrawEid']}..{r['lastDrawEid']}")
