import struct, sys

path = sys.argv[1]
f = open(path, 'rb')
f.read(344)  # 16 magic + 8 ver + 3*60 strings + 140
loc = [struct.unpack('<i', f.read(4))[0] for _ in range(7)]
print("seek table:", loc)
f.seek(loc[2])
pc = struct.unpack('<i', f.read(4))[0]
seek = struct.unpack('<i', f.read(4))[0]
print("pens count:", pc)
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

def d(args, i):
    b = args[i] if i < len(args) else None
    return struct.unpack('d', b)[0] if b and len(b) == 8 else None

def ii(args, i):
    b = args[i] if i < len(args) else None
    return struct.unpack('<i', b)[0] if b and len(b) == 4 else None

def s(args, i):
    b = args[i] if i < len(args) else None
    return b.decode('utf-16', 'ignore').strip('\0') if b else ''

for p in range(min(pc, 10)):
    args = ps(f)
    print(f"pen{p}: label={s(args,1)!r} mark={ii(args,2)} passes={ii(args,4)} "
          f"speed={d(args,5)} power={d(args,6)} freq={d(args,7)} "
          f"start_tc={d(args,9)} end_tc={d(args,10)} nargs={len(args)}")
