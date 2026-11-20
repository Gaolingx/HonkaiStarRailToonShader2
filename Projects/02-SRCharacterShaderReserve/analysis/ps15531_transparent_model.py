"""PS 15531 (translucent hair-card / ornament shell) model, decoded from frame 13063 eid 5361.

The draw is the 219-vertex / 352-triangle mesh that README calls the semi-transparent
piece (eids 5349/5361/5404/5585/5595). On screen it is the glass-bubble hair ornament
at the top edge; the fill pass writes straight into the lit HDR target (RT 5279) with
SrcAlpha/OneMinusSrcAlpha.

Everything below was recovered with debug_pixel (eid 5361). Instruction numbers refer
to the PS 15531 disassembly; the VS-threshold block is from VS 15526.

Decoded constants (PS):
  cb0[87].y   -15.925059   palette ramp base height (world Y)
  cb0[96]     (0, 21.8, 0, 2.17)   specular exponent / intensity
  cb0[97].z   0.502        specular cutoff
  cb0[131]    2.244329 grey  rim colour (HDR)
  cb0[285]    (0, 0.165493, 0.5)  gradient dark colour   (t0.x = 1 -> this)
  cb0[286..288] white            gradient light colour / "above line" colour
  cb0[289]    (0.882075, 0.950156, 1.0)  absorption tint (lit)
  cb0[290]    (1, 1, 1)                  absorption tint (specular)
  cb0[293]    (0.0025, 0.0025, 0.0025, -15.066329)  height fade band / line
  cb0[294]    (-15.066329, -15.066329, 0, 0)
  cb0[297].w  0.06         view bias for flag1
  cb0[298].z  0.935        alpha baseline cut
  cb0[299].z  0.686        alpha below-line term
  cb0[300]    (4.0, 1.37)  rim exponents (x = sharp pow, y = wide pow)
  cb1[4..6]   three light/hair vectors, blended by cb0[292] = (1,1,1);
              only the normalised sum is observable: L = (-0.015033, 0.997023, 0.075623)
  cb2[33]     (1, 0.912130, 0.867925, 0.5)  darkening colour + weight
  cb2[37]     (?, ?, 0.65, 0.5)  palette ramp offset / range
  cb2[38]     (0.933333, 0.886275, 0.862745, 0.5)  palette stop (low)
  cb2[39]     (0.866667, 0.796078, 0.776471, 0.5)  palette stop (mid)
  cb2[40]     unused in this frame ((1 - s1) = 0 everywhere), left as cb2[39]

Other layers of the same mesh (same palette function):
  eid 5349 PS 15507: flat(world_y)      = palette * 1.59 * cb0[287], alpha 0.686,
                                        discards fragments above the fade line
                    cb0[287] = (0.462077, 0.672443, 1.0), cb0[297].z = 0.59, cb0[299].z = 0.686
  eid 5585 PS 15503: rim_layer()        = palette * cb0[291], alpha = (1 - N.V)^4
                    cb0[291] = 1.259696 grey, cb0[300] = (1.0, 4.0), cb0[299].y = 1.0
  eid 5595 PS 15524: outline_layer()    = palette * cb0[290], alpha = 1.0
                    cb0[290] = (0.1320755, 0.0392488, 0.0612818); the captured pass masks colour
  eid 5404 PS 15520: depth-only (returns 0) + extruded shell from VS 15518

Vertex stage supplies:
  v3 = view direction (normal space), v4 = normal (normal space),
  v5.w = height threshold (cb0[293].x + cb0[293].w in this frame), v6.y = world Y,
  v2.x = vertex alpha, v0 = t0 UV (t0.x = 1 on all sampled pixels).

Formula (world Y drives everything; L is the normalised cb1[4..6] sum):
  palette  = 2 * sat(s2*cb2[38]*w38 + sat(s1-s2)*cb2[39]*w39 + (1-s1)*cb2[40]*w40)
  s1       = smoothstep(sat((Y - cb0[87].y) / cb2[37].w)), s2 = smoothstep(sat(2(Y - cb0[87].y - cb2[37].z)))
  gradient = lerp(white, cb0[285], t0.x)                 (cb0[288] when flag1)
  rim      = (1 - N.V)^4,  fres = (1 - N.V)^1.37
  gradient = (1 - rim) * gradient + rim^2 * cb0[131]     (quadratic, not a lerp)
  spec     = smoothstep(sat((sat(N.H)^21.8 - 0.502) * 10)) * cb0[96].w * flag2
  tint     = (absorb * lerp(cb0[289], cb0[290], spec) - 0.1) * (spec + fres) + 0.1
  colour   = palette * lerp(gradient, tint, sat(spec + 0.314 + flag2 + fres))
  alpha    = sat(max(fres - 0.065, 0) + spec + (flag2 ? 0 : 0.686)) * v2.x
  flag1 = (threshold - 0.0006 < Y), flag2 = (threshold < Y), threshold = -15.063829

Usage: python ps15531_transparent_model.py
"""

import io
import math
import os
import re

def sat(x):
    return min(max(x, 0.0), 1.0)

def dot(a, b):
    return sum(x * y for x, y in zip(a, b))

def norm(v):
    length = math.sqrt(dot(v, v))
    return tuple(x / length for x in v)

def lerp(a, b, t):
    if isinstance(a, tuple):
        return tuple(x + (y - x) * t for x, y in zip(a, b))
    return a + (b - a) * t

def smoothstep(x):
    return x * x * (3.0 - 2.0 * x)

CB0 = {
    87: (-3.138887, -15.925059, -272.895264, 0.0),
    96: (0.0, 21.799999, 0.0, 2.17),
    97: (0.0, 0.0, 0.502, 0.0),
    131: (2.244329, 2.244329, 2.244329, 1.0),
    285: (0.0, 0.165493, 0.5, 1.0),
    286: (1.0, 1.0, 1.0, 1.0),
    288: (1.0, 1.0, 1.0, 1.0),
    289: (0.882075, 0.950156, 1.0, 1.0),
    290: (1.0, 1.0, 1.0, 1.0),
    292: (1.0, 1.0, 1.0, 1.0),
    293: (0.0025, 0.0025, 0.0025, -15.066329),
    294: (-15.066329, -15.066329, 0.0, 0.0),
    297: (0.0, 0.0, 0.59, 0.06),
    298: (0.0, 0.0, 0.935, 0.169),
    299: (0.0, 1.0, 0.686, 0.0),
    300: (4.0, 1.37, 0.0, 0.0),
}

CB2 = {
    33: (1.0, 0.912130, 0.867925, 0.5),
    37: (0.0, 0.0, 0.65, 0.5),
    38: (0.933333, 0.886275, 0.862745, 0.5),
    39: (0.866667, 0.796078, 0.776471, 0.5),
    40: (0.866667, 0.796078, 0.776471, 0.5),  # placeholder, (1 - s1) == 0 here
}

# cb0[292] = (1,1,1) applied to cb1[4..6]; only the sum is observable.
LIGHT_DIR = norm((-0.026038289, 1.726894617, 0.130983114))

def height_threshold(sel, mask):
    """VS 15526 instructions 23..35: per-vertex fade line (world Y)."""
    r0 = lerp(CB0[293][0], CB0[293][1], sel)
    r0 = r0 + mask * (CB0[293][2] - r0)
    r1 = lerp(CB0[293][3], CB0[294][1], mask)
    return r0 + r1

def palette(world_y):
    """Instructions 0..20, shared by all four shading variants."""
    u = sat((world_y - CB0[87][1]) / max(CB2[37][3], 0.001))
    s1 = smoothstep(u)
    v = sat(2.0 * ((world_y - CB0[87][1]) - CB2[37][2]))
    s2 = smoothstep(v)
    mid = sat(s1 - s2)
    return tuple(2.0 * sat(s2 * c38 * CB2[38][3] + mid * c39 * CB2[39][3]
                          + (1.0 - s1) * c40 * CB2[40][3])
                 for c38, c39, c40 in zip(CB2[38][:3], CB2[39][:3], CB2[40][:3]))

def shade(normal, view, world_y, t0_x=1.0, vert_alpha=1.0, sel=0.0, mask=0.0):
    """PS 15531 instructions 0..80 (main layer). sel = 0 when vertex alpha > 0."""
    p = palette(world_y)

    # 21..28 gradient colour; flag1 swaps it for the "above line" colour
    thr = height_threshold(sel, mask)
    flag1 = (thr - 0.01 * CB0[297][3]) < world_y
    flag2 = thr < world_y
    g = lerp(CB0[286][:3], CB0[285][:3], t0_x)
    if flag1:
        g = CB0[288][:3]

    # 29..37 Fresnel rim toward cb0[131]
    nv = sat(dot(normal, view))
    lg = math.log2(max(1.0 - nv, 1e-8))
    rim = min(math.exp2(lg * CB0[300][0]), 1.0)          # (1 - N.V)^4
    fres = max(math.exp2(lg * CB0[300][1]), 0.001)       # (1 - N.V)^1.37
    # instr 36/37 multiply cb0[131] by r^2, not by r: g' = (1 - r)g + r^2 * cb0[131]
    g = tuple((1.0 - rim) * c + rim * rim * r for c, r in zip(g, CB0[131][:3]))

    # 38..39 absorption colour
    absorb = tuple(1.0 + CB2[33][3] * (c - 1.0) for c in CB2[33][:3])

    # 49..62 Blinn specular against the blended stylised light
    half = norm(tuple(a + b for a, b in zip(LIGHT_DIR, view)))
    nh = sat(dot(normal, half))
    sp = math.exp2(math.log2(max(nh, 1e-8)) * CB0[96][1])
    x = sat((sp - CB0[97][2]) * 10.0)
    kernel = x * x * (3.0 - 2.0 * x)
    spec = kernel * CB0[96][3] * (1.0 if flag2 else 0.0)

    # 63..73 absorption tint and blend weight
    tint_lit = lerp(CB0[289][:3], CB0[290][:3], spec)
    tint = tuple((a * c - 0.1) * (spec + fres) + 0.1 for a, c in zip(absorb, tint_lit))
    k = sat(spec + (1.0 - CB0[299][2]) + (1.0 if flag2 else 0.0) + fres)
    shaded = tuple(c + k * (t - c) for c, t in zip(g, tint))
    rgb = tuple(pl * s for pl, s in zip(p, shaded))

    # 75..80 alpha
    alpha = sat(max(fres - (1.0 - CB0[298][2]), 0.0) + spec
                + (0.0 if flag2 else CB0[299][2])) * vert_alpha
    return {"flag1": flag1, "flag2": flag2, "rim": rim, "fres": fres,
            "palette": p, "shaded": shaded, "color": rgb + (alpha,)}

# --- the other draws of the same mesh (same palette, different layers) -------------

CB0_287 = (0.462077, 0.672443, 1.0)                    # eid 5349 flat layer colour
CB0_297_Z = 0.59                                       # eid 5349 brightness add
CB0_299_Z = 0.686                                      # eid 5349/5361 below-line alpha
CB0_291 = 1.259696                                     # eid 5585 rim layer colour (grey)
CB0_290_OUTLINE = (0.1320755, 0.0392488, 0.0612818)    # eid 5595 outline colour

def flat(world_y, vert_alpha=1.0, threshold=-15.063829):
    """PS 15507 (eid 5349): palette * (1 + cb0[297].z) * cb0[287], discard above the line."""
    if threshold < world_y:
        return None
    p = palette(world_y)
    return tuple(c * (1.0 + CB0_297_Z) * k for c, k in zip(p, CB0_287)) + (vert_alpha * CB0_299_Z,)

def rim_layer(normal, view, world_y, vert_alpha=1.0):
    """PS 15503 (eid 5585): palette * cb0[291], alpha = (1 - N.V)^4 (cb0[300]=(1,4))."""
    p = palette(world_y)
    fres = max((1.0 - sat(dot(normal, view))) ** 4.0, 0.001)
    return tuple(c * CB0_291 for c in p) + (fres * vert_alpha,)

def outline_layer(world_y, vert_alpha=1.0):
    """PS 15524 (eid 5595): palette * cb0[290], alpha = sat(cb0[299].y)."""
    p = palette(world_y)
    return tuple(c * k for c, k in zip(p, CB0_290_OUTLINE)) + (1.0 * vert_alpha,)

PIXELS = [
    # (x, y) 1850,50: below the fade line, no specular; input from the full trace
    dict(fn="main", name="A (1850,50)",
         normal=(-0.40516403317451477, 0.5469121336936951, 0.7020222544670105),
         view=(0.14880625903606415, 0.6751595735549927, 0.7224935293197632),
         world_y=-15.069393157958984,
         expected=(0.06496578454971313, 0.14191217720508575, 0.29787707328796387, 0.7192324995994568)),
    # 1790,100: above the fade line -> flag2, alpha collapses to the Fresnel term
    dict(fn="main", name="B (1790,100)",
         normal=(0.5505584478378296, 0.19538147747516632, 0.7918654680252075),
         view=(0.12706725299358368, 0.6822180151939392, 0.7200124859809875),
         world_y=-15.05612564086914,
         expected=(0.18083414435386658, 0.17136122286319733, 0.16971103847026825, 0.06582669913768768)),
    # 1830,90: above the line, facing the camera -> alpha 0
    dict(fn="main", name="C (1830,90)",
         normal=(0.17907372117042542, 0.6494989991188049, 0.7050023078918457),
         view=(0.13604655861854553, 0.6752385497093201, 0.7249274849891663),
         world_y=-15.056537628173828,
         expected=(0.09408207982778549, 0.08780487626791, 0.0857090950012207, 0.0)),
    # 1785,60: below the line, grazing view -> largest alpha
    dict(fn="main", name="D (1785,60)",
         normal=(0.14357835054397583, -0.14175209403038025, 0.9543756246566772),
         view=(0.13578873872756958, 0.6871286630630493, 0.7137234210968018),
         world_y=-15.06935977935791,
         expected=(0.17063087224960327, 0.2165420949459076, 0.3210846781730652, 0.902813196182251)),
    # eid 5349 (PS 15507): flat bright layer, same pixel
    dict(fn="flat", name="flat (1850,50)", world_y=-15.086004257202148,
         expected=(0.6524543166160583, 0.8820905089378357, 1.2785927057266235, 0.6859999895095825)),
    # eid 5585 (PS 15503): rim layer, body pixel and silhouette pixel
    dict(fn="rim", name="rim (1850,50)",
         normal=(-0.35515066981315613, 0.565811038017273, 0.7110313773155212),
         view=(0.14880453050136566, 0.6751554608345032, 0.7224934101104736),
         world_y=-15.0682954788208,
         expected=(1.1229419708251953, 1.0450360774993896, 1.018500566482544, 0.0009999871253967285)),
    dict(fn="rim", name="rim (1778,40)",
         normal=(-0.04235444217920303, -0.41384413838386536, 0.881273090839386),
         view=(0.13938546180725098, 0.6904275417327881, 0.7098408937454224),
         world_y=-15.077583312988281,
         expected=(1.1206881999969482, 1.0419867038726807, 1.015583872795105, 0.19682376086711884)),
    # eid 5595 (PS 15524): outline colour layer (masked in the captured pass)
    dict(fn="outline", name="outline (1778,40)", world_y=-15.086871147155762,
         expected=(0.11726845055818558, 0.0323721244931221, 0.04926663637161255, 0.9999999403953552)),
]

FUNCS = {"main": shade, "flat": flat, "rim": rim_layer, "outline": outline_layer}

# --- composite regression (SrcAlpha / OneMinusSrcAlpha over the RT) -----------------
def blend(src_rgba, dst_rgb):
    a = src_rgba[3]
    return tuple(a * s + (1.0 - a) * d for s, d in zip(src_rgba[:3], dst_rgb))

COMPOSITES = [
    # 5349 over the observed dst: postMod (0.664, 0.742, 1.0), 3-decimal readback.
    dict(name="flat over dst", fn="flat", args=dict(world_y=-15.086004257202148),
         dst=(0.695, 0.445, 0.414), expected=(0.664, 0.742, 1.0)),
    # 5361 over the flat result at (1850,50).
    dict(name="main over flat", fn="main",
         args=dict(normal=(-0.40516403317451477, 0.5469121336936951, 0.7020222544670105),
                   view=(0.14880625903606415, 0.6751595735549927, 0.7224935293197632),
                   world_y=-15.069393157958984),
         dst=(0.664, 0.742, 1.0), expected=(0.232, 0.309, 0.492)),
]

# --- shader parity: the ported material defaults must match the decoded constants ----
SHADER_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                           "..", "Shaders", "SRCharacterTransparent.shader")

PASS_ORDER = [
    "SRCharacterTransparentFlat",
    "SRCharacterTransparentShade",
    "SRCharacterTransparentDepthShell",
    "SRCharacterTransparentRim",
    "SRCharacterTransparentOutline",
    "SRCharacterHairDepth",
]

EXPECTED_DEFAULTS = {
    "_SRHeightLine": CB0[293][0] + CB0[293][3],
    "_SRHeightFade": CB0[297][3],
    "_SRPaletteBase": CB0[87][1],
    "_SRPaletteRange": CB2[37][3],
    "_SRPaletteOffset": CB2[37][2],
    "_SRPaletteLow": CB2[38],
    "_SRPaletteMid": CB2[39],
    "_SRPaletteHigh": CB2[40],
    "_SRGradientDark": CB0[285],
    "_SRAboveLineColor": CB0[288],
    "_SRFlatTint": CB0_287 + (1.0,),
    "_SRFlatBrightness": CB0_297_Z,
    "_SRFlatAlpha": CB0_299_Z,
    "_SRMainRimColor": CB0[131],
    "_SRMainRimExponent": CB0[300][0],
    "_SRMainFresnelExponent": CB0[300][1],
    "_SRMainAlphaCut": 1.0 - CB0[298][2],
    "_SRMainSpecExponent": CB0[96][1],
    "_SRMainSpecCutoff": CB0[97][2],
    "_SRMainSpecIntensity": CB0[96][3],
    "_SRMainAbsorbTint": CB2[33],
    "_SRMainAbsorbLit": CB0[289],
    "_SRMainAbsorbSpec": CB0[290],
    "_SRDepthShellExtrude": 0.0,
    "_SRRimLayerTint": (CB0_291, CB0_291, CB0_291, 1.0),
    "_SRRimLayerExponent": 4.0,
    "_SRRimLayerAlpha": CB0[299][1],
    "_SRLayerOutlineColor": CB0_290_OUTLINE + (1.0,),
    "_SRLayerOutlineAlpha": 1.0,
    "_SRStencilRefFlat": 0,
    "_SRStencilRefMain": 24,
    "_SRStencilRefDepth": 10,
    "_SRStencilRefRim": 11,
    "_SRStencilRefOutline": 11,
}

PROPERTY_RE = re.compile(
    r'^\s*(_[A-Za-z0-9_]+)\s*\(\s*"(?:[^"\\]|\\.)*"\s*,\s*'
    r'(?:Float|Int|Color|Vector|Range\([^)]*\)|2D)\s*\)\s*=\s*(.+?)\s*$',
    re.MULTILINE)


def parse_shader(path):
    text = io.open(path, encoding="utf-8-sig").read()
    values = {}
    for name, raw in PROPERTY_RE.findall(text):
        raw = raw.strip()
        if raw.startswith('"'):
            continue   # texture defaults are not numeric
        if raw.startswith("("):
            values[name] = tuple(float(x) for x in raw.strip("()").split(","))
        else:
            values[name] = float(raw)
    order = re.findall(r'"LightMode"\s*=\s*"([^"]+)"', text)
    return values, order


def check_shader(errors, path=SHADER_PATH):
    values, order = parse_shader(path)
    if order != PASS_ORDER:
        errors.append("light-mode order %s != %s" % (order, PASS_ORDER))

    for name, expected in EXPECTED_DEFAULTS.items():
        if name not in values:
            errors.append("shader property %s missing" % name)
            continue
        got = values[name]
        got_tuple = got if isinstance(got, tuple) else (got,)
        exp_tuple = expected if isinstance(expected, tuple) else (expected,)
        if len(got_tuple) != len(exp_tuple):
            errors.append("%s: %s != %s" % (name, got_tuple, exp_tuple))
            continue
        for g, e in zip(got_tuple, exp_tuple):
            if abs(g - e) > 1e-5:
                errors.append("%s: %.7f != %.7f" % (name, g, e))
                break


def main():
    errors = []
    for p in PIXELS:
        fn = FUNCS[p["fn"]]
        args = {k: v for k, v in p.items() if k not in ("fn", "name", "expected")}
        got = fn(**args)
        if got is None:
            errors.append("%s: discarded" % p["name"])
            continue
        color = got["color"] if isinstance(got, dict) else got
        print(p["name"], "color=", tuple(round(c, 6) for c in color))
        for g, e in zip(color, p["expected"]):
            if abs(g - e) > 2e-4:
                errors.append("%s: %.6f != %.6f" % (p["name"], g, e))

    for c in COMPOSITES:
        got = FUNCS[c["fn"]](**c["args"])
        color = got["color"] if isinstance(got, dict) else got
        composited = blend(color, c["dst"])
        print(c["name"], "postMod=", tuple(round(x, 6) for x in composited))
        for g, e in zip(composited, c["expected"]):
            if abs(g - e) > 1e-2:   # capture readback is 3-decimal
                errors.append("%s: %.6f != %.6f" % (c["name"], g, e))

    check_shader(errors)

    if errors:
        raise SystemExit("FAILED: " + "; ".join(errors))
    print("ok: all %d trace pixels, %d composites and shader defaults reproduced"
          % (len(PIXELS), len(COMPOSITES)))

if __name__ == "__main__":
    main()