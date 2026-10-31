import re, sys, xml.etree.ElementTree as ET

VERT_TOKENS = [
    ("POINT", r"([-+]?[0-9]*\.?[0-9]+(?:[eE][-+]?[0-9]+)? [-+]?[0-9]*\.?[0-9]+(?:[eE][-+]?[0-9]+)?)"),
    ("SKIP", r"[ ,\t\n\x09\x0A\x0C\x0D]+"),
    ("VERTEX", r"(V)"),
    ("SFLAG", r"(S)"),
    ("CONTROL", r"(c0x|c0y|c1x|c1y)"),
    ("NUM", r"([-+]?[0-9]*\.?[0-9]+(?:[eE][-+]?[0-9]+)?)"),
]
vert_re = re.compile("|".join("(?P<%s>%s)" % p for p in VERT_TOKENS))

PRIM_TOKENS = [
    ("SKIP", r"[ ,\t\n\x09\x0A\x0C\x0D]+"),
    ("TYPE", r"([BLC])"),
    ("CONNECT", r"(\d+ \d+)"),
    ("CLOSED", r"(LineClosed)"),
]
prim_re = re.compile("|".join("(?P<%s>%s)" % p for p in PRIM_TOKENS))

def tokenize(regex, text):
    p = 0
    n = 0
    while p < len(text):
        m = regex.match(text, p)
        if m is None:
            return None
        p = m.end()
        n += 1
    return n

path = sys.argv[1]
tree = ET.parse(path)
root = tree.getroot()
errors = 0
shapes = 0
for shape in root.iter("Shape"):
    shapes += 1
    vl = shape.find("VertList")
    pl = shape.find("PrimList")
    vtext = vl.text or ""
    ptext = (pl.text or "") if pl is not None else ""
    if tokenize(vert_re, vtext) is None:
        print(f"  BAD VertList in shape {shapes}: {vtext[:80]}")
        errors += 1
    nverts = len(re.findall(r"V[-+0-9]", vtext))
    if ptext not in ("LineClosed", ""):
        if tokenize(prim_re, ptext) is None:
            print(f"  BAD PrimList in shape {shapes}: {ptext[:80]}")
            errors += 1
        else:
            for conn in re.findall(r"(\d+) (\d+)", ptext):
                i0, i1 = int(conn[0]), int(conn[1])
                if i0 >= nverts or i1 >= nverts:
                    print(f"  BAD index {i0}->{i1} (verts={nverts}) in shape {shapes}")
                    errors += 1
cut_indices = {s.attrib.get("CutIndex") for s in root.iter("Shape")}
settings = {cs.find("index").attrib.get("Value") for cs in root.iter("CutSetting")}
missing = cut_indices - settings
if missing:
    print(f"  CutIndex without CutSetting: {missing}")
    errors += 1
print(f"{path}: shapes={shapes}, layers={len(settings)}, errors={errors}")
sys.exit(1 if errors else 0)
