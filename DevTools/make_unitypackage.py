"""Builds a .unitypackage from Assets/HawkesGen without Unity (it can stay open).

A .unitypackage is a gzipped tar with one folder per asset, named by its GUID: asset (the file; none for folders),
asset.meta and pathname.
    python DevTools/make_unitypackage.py <out.unitypackage>
"""
import glob
import io
import os
import re
import sys
import tarfile
import time

out = sys.argv[1] if len(sys.argv) > 1 else 'HawkesGen.unitypackage'
metas = ['Assets/HawkesGen.meta'] + sorted(glob.glob('Assets/HawkesGen/**/*.meta', recursive=True))


def add(t, name, data):
    info = tarfile.TarInfo(name)
    info.size = len(data)
    info.mtime = int(time.time())
    info.mode = 0o644
    t.addfile(info, io.BytesIO(data))


with tarfile.open(out, 'w:gz') as t:
    for m in metas:
        m = m.replace(os.sep, '/')
        meta = open(m, 'rb').read()
        guid = re.search(rb'guid: ([0-9a-f]{32})', meta).group(1).decode()
        path = m[:-len('.meta')]
        add(t, f'{guid}/asset.meta', meta)
        add(t, f'{guid}/pathname', path.encode())
        if os.path.isfile(path):
            add(t, f'{guid}/asset', open(path, 'rb').read())
print(len(metas), 'assets ->', out, os.path.getsize(out), 'bytes')
