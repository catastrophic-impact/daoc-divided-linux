#!/usr/bin/env python3
"""fetch-divided-release.py <tag> [cache-dir] - pull the world data out of a DividedRealms release zip.

The release zip is ~950 MB and holds the finished world DB, the navmeshes and a few data files that are not in git.
This reads only those members, using HTTP range requests, and skips files already cached (safe to re-run).
Result in <cache>/<tag>/: world.sqlite, navmesh/, server-data/ (classic-quests.json etc.), world-updates/, client-patch.json.
Only the Python standard library is used.
"""
import io, json, os, sys, urllib.request, zipfile

REPO_SLUG = 'rambodolphin13/DividedRealms'


class RangeFile(io.RawIOBase):
    def __init__(self, url):
        self.pos = 0
        r = urllib.request.urlopen(urllib.request.Request(url, headers={'Range': 'bytes=0-0'}))
        self.size = int(r.headers['Content-Range'].split('/')[1])
        self.url = r.geturl()

    def seekable(self): return True
    def readable(self): return True
    def tell(self): return self.pos

    def seek(self, o, w=0):
        self.pos = {0: o, 1: self.pos + o, 2: self.size + o}[w]
        return self.pos

    def readinto(self, b):
        n = min(len(b), self.size - self.pos)
        if n <= 0:
            return 0
        for attempt in range(5):
            try:
                req = urllib.request.Request(self.url, headers={'Range': f'bytes={self.pos}-{self.pos + n - 1}'})
                d = urllib.request.urlopen(req, timeout=60).read()
                break
            except OSError:
                if attempt == 4:
                    raise
        b[:len(d)] = d
        self.pos += len(d)
        return len(d)


def main():
    tag = sys.argv[1]
    cache = os.path.abspath(sys.argv[2] if len(sys.argv) > 2 else '.cache/divided')
    out = os.path.join(cache, tag)
    # The release zip's name and top folder changed with DR-dev-0.1 ("...-modern-test.zip" before), so ask GitHub for it.
    api = f'https://api.github.com/repos/{REPO_SLUG}/releases/tags/{tag}'
    with urllib.request.urlopen(urllib.request.Request(api, headers={'User-Agent': 'daoc-divided-linux'})) as r:
        assets = json.load(r)['assets']
    asset = next(a for a in assets if a['name'].endswith('.zip'))
    url = asset['browser_download_url']
    z = zipfile.ZipFile(io.BufferedReader(RangeFile(url), 1 << 20))
    top = z.namelist()[0].split('/')[0]
    root = f'{top}/runtime/'
    want = {
        root + 'data/world-clean.sqlite3.db': 'world.sqlite',
    }
    for prefix, dest in ((root + 'server/navmesh/', 'navmesh/'), (root + 'world-updates/', 'world-updates/')):
        for i in z.infolist():
            if i.filename.startswith(prefix) and not i.filename.endswith('/'):
                want[i.filename] = dest + i.filename[len(prefix):]
    for name in ('classic-quests.json', 'classic-quest-guides.json', 'classic-otd.json'):
        want[root + 'server/' + name] = 'server-data/' + name
    want[f'{top}/tools/connect-local1130.exe'] = 'tools/connect-local1130.exe'
    want[root + 'client-patch.json'] = 'client-patch.json'  # daoc-divided-linux: the client patch the Client tab applies (tui/client_patch.py)
    info = {i.filename: i for i in z.infolist()}
    done = 0
    for src, rel in sorted(want.items()):
        i = info[src]
        dst = os.path.join(out, rel)
        if os.path.exists(dst) and os.path.getsize(dst) == i.file_size:
            continue
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        with z.open(i) as f, open(dst + '.part', 'wb') as o:
            while chunk := f.read(1 << 20):
                o.write(chunk)
        os.replace(dst + '.part', dst)
        done += 1
        if done % 10 == 0:
            print(f'  {done} files fetched', flush=True)
    print(f'{len(want)} files in {out} ({done} fetched now)')


if __name__ == '__main__':
    main()
