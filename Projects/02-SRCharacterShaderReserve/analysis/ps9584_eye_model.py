"""PS 9584 (eye shade) formula model, decoded from frame 13063 eid 5015.

Constants recovered with debug_pixel --trace (one all-reflection pixel and one
refraction pixel); both runs are checked at the bottom. The shader port lives in
Shaders/SRCharacter.shader (SREyeRefract / EyeShadeFragment).

Decoded constants:
  cb3[8].xyz  camera position (world)
  cb3[9].xyz  light colour (2.0, 1.505, 1.008), cb3[9].w = 5 (distance fade)
  cb3[10].x   specular intensity 1.0, cb3[10].y = 1.0 (default mask)
  cb3[11]     (1.0, 3.0, 2.0): spec radius, unused alt attenuation, fade power
  cb3[12..13] extra light term, disabled in this frame (cb3[13].x <= 0)
  cb3[16]     (0, 1): eye mask comes straight from G-buffer normal.a
  cb2[68].x   0.5 (type-0 mask), cb2[78].z = 1.0 (fog blend = off)
  t1 (normal G-buffer): rg = octahedral normal, b = type / 65535, a = eye mask
  t4 tex3403 = screen-space AO (about 0.99), t2 = 3D volume fog LUT
  fog         = t2 LUT factor (0.970703 at pixel A); the shader leaves fog to URP

Usage: python ps9584_eye_model.py
"""

import math

PI = math.pi


def sat(x):
    return min(max(x, 0.0), 1.0)


def dot(a, b):
    return sum(x * y for x, y in zip(a, b))


def norm(v):
    length = math.sqrt(dot(v, v))
    return tuple(x / length for x in v)


def refract_block(D, nl, cnt, vl, k):
    """Literal port of PS 9584 instructions 111..166.

    D = specular reflectance, nl = N.L, cnt = sqrt(1 - k^2) (r5.x after instr 101),
    vl = V.L, k = refraction radius. Returns (A, cosT) for the later refraction terms.
    """
    sn = 2.0 * D * nl - vl
    r7z = k / math.sqrt(max(1.0 - sn * sn, 1e-4))
    r7w = nl - sn * D
    r8w = r7w * r7z
    r9w = 2.0 * nl
    r9w = r9w * nl - 1.0
    cw = (-sn * vl + r9w) * r7z
    r9w = 1.0 - D * D - nl * nl - vl * vl
    r7y = 2.0 * D * nl
    r7y = math.sqrt(max(r7y * vl + r9w, 1e-4)) * r7z
    r9w = nl * (2.0 * r7y)
    r10x = D * cnt + nl
    r7z = r7z * r7w + r10x
    r7w = vl * cnt + cw + 1.0
    r10y = r7w * r7y
    r10z = r7w * r7z
    r10w = r7z * r9w
    r11x = r10z * (r10w * 0.25 - 0.5 * r10y)
    r10w *= -2.0 * r10y + r10w
    r10y = r10y * r10y + r10w
    r10w = r7w * r7w
    r7w = (-0.5 * (vl * cnt + r7w) - 0.5) * r10z
    r7w = r10x * r10w + r7w
    r7z = r7z * r7w + r10y
    r7w = 2.0 * r11x
    r10x = max(r7z * r7z + r11x * r11x, 0.001)
    r7w /= r10x
    r7z *= r7w
    r7w = 1.0 - r7w * r11x
    r7y = r7w * r8w + r7y * r7z
    cw = r7w * cw + r9w * r7z
    r7y = D * cnt + r7y
    r5x = vl * cnt + cw
    r5w = 1.0 / math.sqrt(max(2.0 * r5x + 2.0, 1e-4))
    return sat(r5w * (nl + r7y)), sat(r5w * (r5x + 1.0))


def eye_shade(mask, albedo, normal, light_dir, cam_pos, position, light_color,
              ao=1.0, spec_radius=1.0, fade_distance=5.0, iris_base=0.04, fog=1.0):
    """Returns the additive eye colour and the intermediate terms used by the trace."""
    to_cam = tuple(c - p for c, p in zip(cam_pos, position))
    dist = math.sqrt(dot(to_cam, to_cam))
    view = norm(to_cam)
    nl = sat(dot(normal, light_dir))
    nv_raw = dot(normal, view)
    nv = sat(nv_raw)
    vl = dot(view, light_dir)

    fade01 = sat(1.0 - (dist / fade_distance) ** 2)
    fade = fade01 * fade01

    spec_t = math.sqrt(sat(spec_radius * spec_radius / (dist * dist + 1.0)))
    spec_x = max(nv_raw, -spec_t) + spec_t
    D = sat(spec_x * spec_x / max(4.0 * spec_t, 1e-6))

    half_cos = math.sqrt(sat((1.0 + vl) * 0.5))
    fresnel5 = (1.0 - half_cos) ** 5
    tint = iris_base + (1.0 - iris_base) * fresnel5

    radius = sat(mask * spec_radius / dist)
    strength = sat((1.0 - mask) ** 2 + 1.0 / (3.6 * nl + 0.4))

    cnt = math.sqrt(sat(1.0 - radius * radius))
    sn = 2.0 * D * nl - vl
    if sn >= cnt:
        A, cos_t, branch = 1.0, abs(nl), "TIR"
    else:
        A, cos_t = refract_block(D, nl, cnt, vl, radius)
        branch = "REFR"

    s2 = strength * strength
    q = math.sqrt(sat(s2 + nl * nl * (1.0 - s2)))
    p = math.sqrt(sat(D * D * (1.0 - s2) + s2))
    a2 = A * A * (s2 - 1.0) + 1.0
    refraction = sat((s2 / (2.0 * PI)) / max((D * q + nl * p) * a2 * a2, 1e-5))
    refraction_color = tuple(sat(refraction * tint) for _ in range(3))

    transmission = strength / (((3.0 * math.sqrt(strength) + radius) * radius * 0.25) / (cos_t + 0.001) + strength)

    light = tuple(c * fade * ao for c in light_color)
    spec = tuple(D * a * l for a, l in zip(albedo, light))
    refr = tuple(rc * nv * l * transmission for rc, l in zip(refraction_color, light))
    return {
        "branch": branch,
        "D": D,
        "A": A,
        "cosT": cos_t,
        "dist": dist,
        "fade": fade,
        "s": strength,
        "k": radius,
        "color": tuple((s + r) * fog for s, r in zip(spec, refr)),
    }


# Pixel A: 3072x1920, (2582, 1410), eid 5015 -> trace output (0.0578268, 0.091822, 0.0645218)
PIXEL_A = dict(
    mask=0.624216,
    albedo=(0.0932617, 0.208984, 0.219727),
    normal=norm((0.00421718, -0.0044635, -0.999981)),
    light_dir=norm((0.957698, 0.022176, -0.28692)),
    cam_pos=(-56.5450, -16.3289, -256.9334),
    position=(-55.8676, -15.8489, -256.761),
    light_color=(2.0, 1.50462, 1.00804),
    ao=0.992401,
    fog=0.970703,
    expected=(0.0578268, 0.091822, 0.0645218),
)

# Pixel B: (2620, 1460), eid 5015 -> refraction branch, r9.x = 1, r0.w(instr 166) = 0.528781
PIXEL_B = dict(
    mask=0.630472,
    albedo=(0.0480957, 0.045166, 0.045166),
    normal=norm((0.647062, -0.111052, -0.754306)),
    light_dir=norm((0.955012, 0.00747051, -0.296473)),
    cam_pos=(-56.5450, -16.3289, -256.9334),
    position=(-20.4526, -14.7474, -267.172),
    light_color=(2.0, 1.50462, 1.00804),
    ao=0.983337,
)


def main():
    expected = PIXEL_A.pop("expected")
    a = eye_shade(**PIXEL_A)
    b = eye_shade(**PIXEL_B)
    print("pixel A:", {k: (round(v, 6) if isinstance(v, float) else v) for k, v in a.items()})
    print("pixel B:", {k: (round(v, 6) if isinstance(v, float) else v) for k, v in b.items()})

    errors = []
    if a["branch"] != "TIR":
        errors.append("pixel A branch")
    for got, want in zip(a["color"], expected):
        if abs(got - want) > 1e-4:
            errors.append("pixel A colour %.6f != %.6f" % (got, want))
    if b["branch"] != "REFR":
        errors.append("pixel B branch")
    if abs(b["A"] - 1.0) > 1e-3 or abs(b["cosT"] - 0.528781) > 1e-3:
        errors.append("pixel B refraction terms")

    if errors:
        raise SystemExit("FAILED: " + "; ".join(errors))
    print("ok: both trace pixels reproduced")


if __name__ == "__main__":
    main()