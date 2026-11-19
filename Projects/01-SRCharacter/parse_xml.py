"""Parse RenderDoc XML export into structured per-event state and pass groups.

Outputs (raw/):
  xml_resources.json  - textures/buffers/views/shaders/input layouts
  xml_events.json     - every action with full D3D11 state snapshot
  xml_passes.json     - pass grouping summary
"""
import json
import os
import re
import sys
import xml.etree.ElementTree as ET
from collections import OrderedDict

SRC = r"E:\FileRecv\StarRail_2024.12.09_01.39_frame13063.xml"
OUT = os.path.dirname(os.path.abspath(__file__))
RAW = os.path.join(OUT, "raw")
os.makedirs(RAW, exist_ok=True)


def local(tag):
    return tag.rsplit("}", 1)[-1]


def kids(elem):
    return [c for c in elem if isinstance(c.tag, str)]


def child(elem, name):
    for c in elem:
        if isinstance(c.tag, str) and c.get("name") == name:
            return c
    return None


def find_deep(elem, name):
    for c in elem.iter():
        if c is not elem and isinstance(c.tag, str) and c.get("name") == name:
            return c
    return None


def text_of(elem):
    return (elem.text or "").strip() if elem is not None else None


def val(elem, name, default=None):
    if elem is None:
        return default
    c = child(elem, name)
    if c is None:
        return default
    return text_of(c)


def int_of(elem, name, default=0):
    v = val(elem, name)
    try:
        return int(v)
    except (TypeError, ValueError):
        return default


def arr_ids(elem, name):
    """Array of ResourceId children -> list[int]."""
    a = child(elem, name)
    if a is None:
        return []
    out = []
    for c in a:
        if isinstance(c.tag, str) and local(c.tag) == "ResourceId":
            t = text_of(c)
            out.append(int(t) if t else 0)
    return out


def arr_structs(elem, name):
    a = child(elem, name)
    if a is None:
        return []
    return [c for c in a if isinstance(c.tag, str)]


def arr_texts(elem, name):
    a = child(elem, name)
    if a is None:
        return []
    out = []
    for c in a:
        if isinstance(c.tag, str) and len(c) == 0:
            out.append(text_of(c))
    return out


def enum_str(elem, name, default=None):
    if elem is None:
        return default
    c = child(elem, name)
    if c is None:
        return default
    return c.get("string", text_of(c))


# ---------------- resource databases ----------------
tex = {}
buf = {}
srv = {}
rtv = {}
dsv = {}
uav = {}
shaders = {}      # id -> {stage, bytecode}
layouts = {}      # id -> [ {semantic, index, format, slot, offset} ]
shader_out_decl = {}

STATE_GRAPHICS = ("DrawIndexed", "Draw", "DrawIndexedInstanced", "DrawInstanced",
                  "DrawIndexedInstancedIndirect", "DrawInstancedIndirect",
                  "DrawAuto")
STATE_ACTION_NAMES = set(STATE_GRAPHICS) | {
    "Dispatch", "DispatchIndirect", "ClearRenderTargetView", "ClearDepthStencilView",
    "CopyResource", "CopySubresourceRegion", "ResolveSubresource",
    "UpdateSubresource", "CopyStructureCount",
}

STAGE_NAMES = {
    "VS": "vs", "HS": "hs", "DS": "ds", "GS": "gs", "PS": "ps", "CS": "cs",
}


def new_state():
    return {
        "rtv": [],
        "dsv": 0,
        "viewport": None,
        "shaders": {"vs": 0, "hs": 0, "ds": 0, "gs": 0, "ps": 0, "cs": 0},
        "srv": {s: {} for s in ("vs", "hs", "ds", "gs", "ps", "cs")},
        "cb": {s: {} for s in ("vs", "hs", "ds", "gs", "ps", "cs")},
        "samplers": {s: {} for s in ("vs", "hs", "ds", "gs", "ps", "cs")},
        "uav": {},
        "so": [],
        "layout": 0,
        "topology": "",
        "vb": [],
        "ib": 0,
        "blend": 0,
        "depthstencil": 0,
        "stencilref": 0,
        "rasterizer": 0,
        "render_targets_null": False,
    }


def snapshot(st):
    return {
        "rtv": list(st["rtv"]),
        "dsv": st["dsv"],
        "viewport": dict(st["viewport"]) if st["viewport"] else None,
        "shaders": dict(st["shaders"]),
        "srv": {s: {str(k): v for k, v in st["srv"][s].items() if v} for s in st["srv"]},
        "cb": {s: {str(k): v for k, v in st["cb"][s].items() if v} for s in st["cb"]},
        "samplers": {s: {str(k): v for k, v in st["samplers"][s].items() if v} for s in st["samplers"]},
        "uav": {str(k): v for k, v in st["uav"].items() if v},
        "so": list(st["so"]),
        "layout": st["layout"],
        "topology": st["topology"],
        "vb": list(st["vb"]),
        "ib": st["ib"],
        "blend": st["blend"],
        "depthstencil": st["depthstencil"],
        "rasterizer": st["rasterizer"],
    }


def main():
    tree = ET.parse(SRC)
    root = tree.getroot()
    chunks = root.find("chunks")
    st = new_state()
    events = []
    maps = {}  # buffer id -> last written data buffer id
    order = 0
    t0 = None
    for ch in chunks:
        if not isinstance(ch.tag, str) or local(ch.tag) != "chunk":
            continue
        name = ch.get("name", "")
        ci = int(ch.get("chunkIndex", "0"))
        short = name.split("::")[-1]
        order += 1

        if short == "CreateBuffer":
            bid = int(text_of(child(ch, "pBuffer")) or 0)
            desc = child(ch, "pDesc")
            if bid:
                buf[bid] = {
                    "byteWidth": int_of(desc, "ByteWidth"),
                    "usage": enum_str(desc, "Usage"),
                    "bind": enum_str(desc, "BindFlags"),
                    "cpu": enum_str(desc, "CPUAccessFlags"),
                    "misc": enum_str(desc, "MiscFlags"),
                    "stride": int_of(desc, "StructureByteStride"),
                }
        elif short == "CreateTexture2D":
            tid = int(text_of(child(ch, "pTexture")) or 0)
            d = child(ch, "Descriptor")
            if tid:
                tex[tid] = {
                    "w": int_of(d, "Width"), "h": int_of(d, "Height"),
                    "mips": int_of(d, "MipLevels", 1), "array": int_of(d, "ArraySize", 1),
                    "fmt": enum_str(d, "Format"),
                    "usage": enum_str(d, "Usage"),
                    "bind": enum_str(d, "BindFlags"),
                    "misc": enum_str(d, "MiscFlags"),
                }
        elif short == "CreateShaderResourceView":
            vid = int(text_of(child(ch, "pView")) or 0)
            d = child(ch, "pDesc")
            if vid:
                srv[vid] = {
                    "res": int(text_of(child(ch, "pResource")) or 0),
                    "fmt": enum_str(d, "Format") if d is not None else None,
                    "dim": enum_str(d, "ViewDimension") if d is not None else None,
                }
        elif short == "CreateRenderTargetView":
            vid = int(text_of(child(ch, "pView")) or 0)
            d = child(ch, "pDesc")
            if vid:
                rtv[vid] = {
                    "res": int(text_of(child(ch, "pResource")) or 0),
                    "fmt": enum_str(d, "Format") if d is not None else None,
                }
        elif short == "CreateDepthStencilView":
            vid = int(text_of(child(ch, "pView")) or 0)
            d = child(ch, "pDesc")
            if vid:
                dsv[vid] = {
                    "res": int(text_of(child(ch, "pResource")) or 0),
                    "fmt": enum_str(d, "Format") if d is not None else None,
                    "dim": enum_str(d, "ViewDimension") if d is not None else None,
                }
        elif short == "CreateUnorderedAccessView":
            vid = int(text_of(child(ch, "pView")) or 0)
            d = child(ch, "pDesc")
            if vid:
                uav[vid] = {
                    "res": int(text_of(child(ch, "pResource")) or 0),
                    "fmt": enum_str(d, "Format") if d is not None else None,
                    "dim": enum_str(d, "ViewDimension") if d is not None else None,
                }
        elif short in ("CreateVertexShader", "CreatePixelShader", "CreateComputeShader",
                       "CreateHullShader", "CreateDomainShader", "CreateGeometryShader",
                       "CreateGeometryShaderWithStreamOutput"):
            sid = int(text_of(child(ch, "pShader")) or 0)
            if sid:
                shaders[sid] = {"stage": short.replace("Create", "").replace("WithStreamOutput", "")}
            if short == "CreateGeometryShaderWithStreamOutput":
                decl = []
                entries = find_deep(ch, "pSODeclaration")
                if entries is not None:
                    for s in entries:
                        if isinstance(s.tag, str):
                            sem = val(s, "SemanticName")
                            if sem:
                                decl.append({
                                    "sem": sem, "idx": int_of(s, "SemanticIndex"),
                                    "slot": int_of(s, "Stream"), "component": int_of(s, "ComponentCount"),
                                })
                if sid:
                    shader_out_decl[sid] = decl
        elif short == "CreateInputLayout":
            lid = int(text_of(child(ch, "pInputLayout")) or 0)
            elems = []
            arr = child(ch, "pInputElementDescs")
            if arr is not None:
                for e in arr:
                    if not isinstance(e.tag, str):
                        continue
                    elems.append({
                        "sem": val(e, "SemanticName"),
                        "idx": int_of(e, "SemanticIndex"),
                        "fmt": enum_str(e, "Format"),
                        "slot": int_of(e, "InputSlot"),
                        "off": int_of(e, "AlignedByteOffset"),
                        "class": enum_str(e, "InputSlotClass"),
                        "step": int_of(e, "InstanceDataStepRate"),
                    })
            if lid:
                layouts[lid] = elems
        elif short == "VSSetShader":
            st["shaders"]["vs"] = int(text_of(child(ch, "pShader")) or 0)
        elif short == "PSSetShader":
            st["shaders"]["ps"] = int(text_of(child(ch, "pShader")) or 0)
        elif short == "GSSetShader":
            st["shaders"]["gs"] = int(text_of(child(ch, "pShader")) or 0)
        elif short == "CSSetShader":
            st["shaders"]["cs"] = int(text_of(child(ch, "pShader")) or 0)
        elif short == "HSSetShader":
            st["shaders"]["hs"] = int(text_of(child(ch, "pShader")) or 0)
        elif short == "DSSetShader":
            st["shaders"]["ds"] = int(text_of(child(ch, "pShader")) or 0)
        elif short == "IASetInputLayout":
            st["layout"] = int(text_of(child(ch, "pInputLayout")) or 0)
        elif short == "IASetPrimitiveTopology":
            st["topology"] = enum_str(ch, "Topology") or ""
        elif short == "IASetVertexBuffers":
            start = int_of(ch, "StartSlot")
            ids = arr_ids(ch, "ppVertexBuffers")
            strides = [int(x) for x in arr_texts(ch, "pStrides") if x]
            offsets = [int(x) for x in arr_texts(ch, "pOffsets") if x]
            vb = [v for v in st["vb"] if v["slot"] < start]
            for i, v in enumerate(ids):
                vb.append({"slot": start + i, "buf": v,
                           "stride": strides[i] if i < len(strides) else 0,
                           "offset": offsets[i] if i < len(offsets) else 0})
            st["vb"] = vb
        elif short == "IASetIndexBuffer":
            st["ib"] = int(text_of(child(ch, "pIndexBuffer")) or 0)
        elif short == "RSSetViewports":
            vps = []
            for s in arr_structs(ch, "pViewports"):
                vps.append({
                    "x": float(val(s, "TopLeftX", 0)), "y": float(val(s, "TopLeftY", 0)),
                    "w": float(val(s, "Width", 0)), "h": float(val(s, "Height", 0)),
                })
            st["viewport"] = vps[0] if len(vps) == 1 else (vps or None)
        elif short == "RSSetState":
            st["rasterizer"] = int(text_of(child(ch, "pRasterizerState")) or 0)
        elif short == "OMSetBlendState":
            st["blend"] = int(text_of(child(ch, "pBlendState")) or 0)
        elif short == "OMSetDepthStencilState":
            st["depthstencil"] = int(text_of(child(ch, "pDepthStencilState")) or 0)
            st["stencilref"] = int_of(ch, "StencilRef")
        elif short == "OMSetRenderTargets":
            st["rtv"] = [v for v in arr_ids(ch, "ppRenderTargetViews")]
            st["dsv"] = int(text_of(child(ch, "pDepthStencilView")) or 0)
            st["uav"] = {}
        elif short == "OMSetRenderTargetsAndUnorderedAccessViews":
            st["rtv"] = [v for v in arr_ids(ch, "ppRenderTargetViews")]
            st["dsv"] = int(text_of(child(ch, "pDepthStencilView")) or 0)
            start = int_of(ch, "UAVStartSlot")
            ids = arr_ids(ch, "ppUnorderedAccessViews")
            st["uav"] = {start + i: v for i, v in enumerate(ids) if v}
        elif re.fullmatch(r"(VS|HS|DS|GS|PS|CS)SetShaderResources", short):
            stage = STAGE_NAMES[short[:2]]
            start = int_of(ch, "StartSlot")
            ids = arr_ids(ch, "ppShaderResourceViews")
            for i, v in enumerate(ids):
                slot = start + i
                if v:
                    st["srv"][stage][slot] = v
                else:
                    st["srv"][stage].pop(slot, None)
        elif re.fullmatch(r"(VS|HS|DS|GS|PS|CS)SetConstantBuffers1?", short):
            stage = STAGE_NAMES[short[:2]]
            start = int_of(ch, "StartSlot")
            ids = arr_ids(ch, "ppConstantBuffers")
            for i, v in enumerate(ids):
                slot = start + i
                if v:
                    st["cb"][stage][slot] = v
                else:
                    st["cb"][stage].pop(slot, None)
        elif re.fullmatch(r"(VS|HS|DS|GS|PS|CS)SetSamplers", short):
            stage = STAGE_NAMES[short[:2]]
            start = int_of(ch, "StartSlot")
            ids = arr_ids(ch, "ppSamplers")
            for i, v in enumerate(ids):
                slot = start + i
                if v:
                    st["samplers"][stage][slot] = v
                else:
                    st["samplers"][stage].pop(slot, None)
        elif short == "CSSetUnorderedAccessViews":
            start = int_of(ch, "StartSlot")
            ids = arr_ids(ch, "ppUnorderedAccessViews")
            for i, v in enumerate(ids):
                slot = start + i
                if v:
                    st["uav"][slot] = v
                else:
                    st["uav"].pop(slot, None)
        elif short == "SOSetTargets":
            st["so"] = arr_ids(ch, "ppSOTargets")
        elif short == "Map":
            rid = int(text_of(child(ch, "pResource")) or 0)
            maps["pending"] = rid
        elif short == "Unmap":
            rid = int(text_of(child(ch, "pResource")) or 0)
            data = child(ch, "MapWrittenData")
            if rid and data is not None:
                try:
                    maps[rid] = int(text_of(data))
                except (TypeError, ValueError):
                    pass
        elif short in STATE_ACTION_NAMES:
            rec = {
                "order": order, "chunkIndex": ci, "kind": short,
                "state": snapshot(st),
            }
            if short == "DrawIndexed":
                rec["args"] = {"indexCount": int_of(ch, "IndexCount"),
                               "startIndex": int_of(ch, "StartIndexLocation"),
                               "baseVertex": int_of(ch, "BaseVertexLocation")}
            elif short == "Draw":
                rec["args"] = {"vertexCount": int_of(ch, "VertexCount"),
                               "startVertex": int_of(ch, "StartVertexLocation")}
            elif short == "DrawIndexedInstanced":
                rec["args"] = {"indexCount": int_of(ch, "IndexCountPerInstance"),
                               "instances": int_of(ch, "InstanceCount"),
                               "startIndex": int_of(ch, "StartIndexLocation"),
                               "baseVertex": int_of(ch, "BaseVertexLocation"),
                               "startInstance": int_of(ch, "StartInstanceLocation")}
            elif short == "DrawIndexedInstancedIndirect":
                rec["args"] = {"argsBuffer": int(text_of(child(ch, "pBufferForArgs")) or 0),
                               "argsOffset": int_of(ch, "AlignedByteOffsetForArgs")}
            elif short == "Dispatch":
                rec["args"] = {"x": int_of(ch, "ThreadGroupCountX"),
                               "y": int_of(ch, "ThreadGroupCountY"),
                               "z": int_of(ch, "ThreadGroupCountZ")}
            elif short == "ClearRenderTargetView":
                rec["args"] = {"view": int(text_of(child(ch, "pRenderTargetView")) or 0),
                               "color": [float(x) for x in arr_texts(ch, "ColorRGBA")]}
            elif short == "ClearDepthStencilView":
                rec["args"] = {"view": int(text_of(child(ch, "pDepthStencilView")) or 0),
                               "depth": val(ch, "Depth"), "stencil": val(ch, "Stencil"),
                               "flags": enum_str(ch, "ClearFlags")}
            elif short == "CopyResource":
                rec["args"] = {"src": int(text_of(child(ch, "pSrcResource")) or 0),
                               "dst": int(text_of(child(ch, "pDstResource")) or 0)}
            elif short == "CopySubresourceRegion":
                rec["args"] = {"src": int(text_of(child(ch, "pSrcResource")) or 0),
                               "dst": int(text_of(child(ch, "pDstResource")) or 0),
                               "dstSub": int_of(ch, "DstSubresource")}
            elif short == "UpdateSubresource":
                rec["args"] = {"dst": int(text_of(child(ch, "pDstResource")) or 0),
                               "sub": int_of(ch, "DstSubresource"),
                               "box": [int_of(child(ch, "pDstBox"), k) for k in ("left", "top", "right", "bottom")] if child(ch, "pDstBox") is not None else None,
                               "contents": text_of(child(ch, "Contents")),
                               "len": int_of(ch, "ContentsLength")}
            events.append(rec)

    # ---- pass grouping ----
    passes = []
    cur = None

    def rt_sig(rec):
        s = rec["state"]
        rtv_res = tuple(rtv.get(v, {}).get("res", v) for v in s["rtv"])
        dsv_res = dsv.get(s["dsv"], {}).get("res", s["dsv"])
        vp = s["viewport"]
        vpsig = (round(vp["x"]), round(vp["y"]), round(vp["w"]), round(vp["h"])) if vp else None
        return (rtv_res, dsv_res, vpsig)

    for rec in events:
        kind = rec["kind"]
        is_compute = kind in ("Dispatch", "DispatchIndirect")
        sig = ("compute",) if is_compute else rt_sig(rec)
        if cur is None or cur["sig"] != sig:
            cur = {
                "index": len(passes), "sig": sig,
                "rtvViews": rec["state"]["rtv"], "dsvView": rec["state"]["dsv"],
                "viewport": rec["state"]["viewport"],
                "startEvent": rec["chunkIndex"], "endEvent": rec["chunkIndex"],
                "draws": [], "dispatches": [], "clears": [], "copies": [],
                "programs": OrderedDict(),
                "totalIndices": 0,
            }
            passes.append(cur)
        cur["endEvent"] = rec["chunkIndex"]
        if kind in ("Dispatch", "DispatchIndirect"):
            cur["dispatches"].append(rec["chunkIndex"])
        elif kind.startswith("Clear"):
            cur["clears"].append(rec)
        elif kind.startswith("Copy") or kind == "ResolveSubresource":
            cur["copies"].append(rec)
        elif kind in STATE_GRAPHICS:
            s = rec["state"]
            key = (s["shaders"]["vs"], s["shaders"]["ps"], s["shaders"]["gs"],
                   s["shaders"]["hs"], s["shaders"]["ds"], s["layout"],
                   s["blend"], s["depthstencil"], s["rasterizer"])
            p = cur["programs"].get(key)
            if p is None:
                p = {"vs": key[0], "ps": key[1], "gs": key[2], "hs": key[3], "ds": key[4],
                     "layout": key[5], "blend": key[6], "depthstencil": key[7],
                     "rasterizer": key[8], "draws": [], "indices": 0}
                cur["programs"][key] = p
            p["draws"].append(rec["chunkIndex"])
            n = rec.get("args", {}).get("indexCount") or rec.get("args", {}).get("vertexCount") or 0
            p["indices"] += n
            cur["totalIndices"] += n
            cur["draws"].append(rec["chunkIndex"])

    for p in passes:
        p["programs"] = list(p["programs"].values())

    def dump(path, obj):
        with open(path, "w", encoding="utf-8") as f:
            json.dump(obj, f, indent=1)

    dump(os.path.join(RAW, "xml_resources.json"), {
        "textures": {str(k): v for k, v in tex.items()},
        "buffers": {str(k): v for k, v in buf.items()},
        "srvs": {str(k): v for k, v in srv.items()},
        "rtvs": {str(k): v for k, v in rtv.items()},
        "dsvs": {str(k): v for k, v in dsv.items()},
        "uavs": {str(k): v for k, v in uav.items()},
        "shaders": {str(k): v for k, v in shaders.items()},
        "layouts": {str(k): v for k, v in layouts.items()},
        "soDecl": {str(k): v for k, v in shader_out_decl.items()},
        "mapWrites": {str(k): v for k, v in maps.items() if k != "pending"},
    })
    dump(os.path.join(RAW, "xml_events.json"), events)
    dump(os.path.join(RAW, "xml_passes.json"), passes)
    print("chunks parsed, events:", len(events), "passes:", len(passes))


if __name__ == "__main__":
    main()
