"""Parse state object definitions (blend/depth-stencil/rasterizer/sampler) from XML."""
import json
import os
import xml.etree.ElementTree as ET

SRC = r"E:\FileRecv\StarRail_2024.12.09_01.39_frame13063.xml"
OUT = os.path.dirname(os.path.abspath(__file__))
RAW = os.path.join(OUT, "raw")


def local(tag):
    return tag.rsplit("}", 1)[-1]


def child(elem, name):
    for c in elem:
        if isinstance(c.tag, str) and c.get("name") == name:
            return c
    return None


def val(elem, name, default=None):
    if elem is None:
        return default
    c = child(elem, name)
    return (c.text or "").strip() if c is not None else default


def enum_str(elem, name, default=None):
    if elem is None:
        return default
    c = child(elem, name)
    return c.get("string", (c.text or "").strip()) if c is not None else default


def int_of(elem, name, default=0):
    try:
        return int(val(elem, name))
    except (TypeError, ValueError):
        return default


states = {"blend": {}, "depthStencil": {}, "rasterizer": {}, "sampler": {}}

for chunk in ET.parse(SRC).getroot().find("chunks"):
    if not isinstance(chunk.tag, str):
        continue
    name = chunk.get("name", "").split("::")[-1]
    if name == "CreateBlendState":
        sid = int(val(chunk, "pState") or 0)
        d = child(chunk, "Descriptor")
        rts = []
        arr = child(d, "RenderTarget") if d is not None else None
        if arr is not None:
            for rt in arr:
                if not isinstance(rt.tag, str):
                    continue
                rts.append({
                    "blendEnable": val(rt, "BlendEnable") == "true" or val(rt, "BlendEnable") == "1",
                    "srcBlend": enum_str(rt, "SrcBlend"),
                    "dstBlend": enum_str(rt, "DestBlend"),
                    "blendOp": enum_str(rt, "BlendOp"),
                    "srcBlendAlpha": enum_str(rt, "SrcBlendAlpha"),
                    "dstBlendAlpha": enum_str(rt, "DestBlendAlpha"),
                    "blendOpAlpha": enum_str(rt, "BlendOpAlpha"),
                    "writeMask": enum_str(rt, "RenderTargetWriteMask") or val(rt, "RenderTargetWriteMask"),
                })
        states["blend"][sid] = {
            "alphaToCoverage": val(d, "AlphaToCoverageEnable"),
            "independentBlend": val(d, "IndependentBlendEnable"),
            "renderTargets": rts,
        }
    elif name == "CreateDepthStencilState":
        sid = int(val(chunk, "pState") or 0)
        d = child(chunk, "Descriptor")
        states["depthStencil"][sid] = {
            "depthEnable": val(d, "DepthEnable"),
            "depthWriteMask": enum_str(d, "DepthWriteMask"),
            "depthFunc": enum_str(d, "DepthFunc"),
            "stencilEnable": val(d, "StencilEnable"),
            "stencilReadMask": val(d, "StencilReadMask"),
            "stencilWriteMask": val(d, "StencilWriteMask"),
            "frontFace": {
                "func": enum_str(child(d, "FrontFace") if d is not None else None, "StencilFunc"),
                "depthFail": enum_str(child(d, "FrontFace") if d is not None else None, "StencilDepthFailOp"),
                "pass": enum_str(child(d, "FrontFace") if d is not None else None, "StencilPassOp"),
                "fail": enum_str(child(d, "FrontFace") if d is not None else None, "StencilFailOp"),
            },
            "backFace": {
                "func": enum_str(child(d, "BackFace") if d is not None else None, "StencilFunc"),
                "depthFail": enum_str(child(d, "BackFace") if d is not None else None, "StencilDepthFailOp"),
                "pass": enum_str(child(d, "BackFace") if d is not None else None, "StencilPassOp"),
                "fail": enum_str(child(d, "BackFace") if d is not None else None, "StencilFailOp"),
            },
        }
    elif name == "CreateRasterizerState":
        sid = int(val(chunk, "pState") or 0)
        d = child(chunk, "Descriptor")
        states["rasterizer"][sid] = {
            "fillMode": enum_str(d, "FillMode"),
            "cullMode": enum_str(d, "CullMode"),
            "frontCCW": val(d, "FrontCounterClockwise"),
            "depthBias": val(d, "DepthBias"),
            "slopeScaledDepthBias": val(d, "SlopeScaledDepthBias"),
            "depthClipEnable": val(d, "DepthClipEnable"),
            "scissorEnable": val(d, "ScissorEnable"),
            "multisampleEnable": val(d, "MultisampleEnable"),
        }
    elif name == "CreateSamplerState":
        sid = int(val(chunk, "pState") or 0)
        d = child(chunk, "Descriptor")
        states["sampler"][sid] = {
            "filter": enum_str(d, "Filter"),
            "addressU": enum_str(d, "AddressU"),
            "addressV": enum_str(d, "AddressV"),
            "addressW": enum_str(d, "AddressW"),
            "maxLOD": val(d, "MaxLOD"),
            "comparisonFunc": enum_str(d, "ComparisonFunc"),
        }

with open(os.path.join(RAW, "state_objects.json"), "w", encoding="utf-8") as f:
    json.dump(states, f, indent=1)
print({k: len(v) for k, v in states.items()})
