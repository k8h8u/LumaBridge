import struct, sys

path = sys.argv[1]
pen_idx = int(sys.argv[2]) if len(sys.argv) > 2 else 0
f = open(path, 'rb')
f.read(344)
loc = [struct.unpack('<i', f.read(4))[0] for _ in range(7)]
f.seek(loc[2])
pc = struct.unpack('<i', f.read(4))[0]
seek = struct.unpack('<i', f.read(4))[0]
f.seek(seek)

def ps(f):
    cnt = struct.unpack('<i', f.read(4))[0]
    out = []
    for i in range(cnt):
        ln = struct.unpack('<i', f.read(4))[0]
        if ln == -1:
            break
        out.append(f.read(ln))
    return out

for p in range(pen_idx + 1):
    args = ps(f)

for i, b in enumerate(args):
    if len(b) == 2:
        v = struct.unpack('<H', b)[0]
    elif len(b) == 4:
        v = struct.unpack('<i', b)[0]
    elif len(b) == 8:
        v = struct.unpack('<d', b)[0]
    elif len(b) == 16:
        v = struct.unpack('<2d', b)
    elif len(b) == 60:
        v = b.decode('utf-16', 'ignore').strip('\0')
    elif len(b) == 72:
        v = 'matrix'
    else:
        v = f'<{len(b)} bytes>'
    print(f"  [{i}] len={len(b)}: {v}")
