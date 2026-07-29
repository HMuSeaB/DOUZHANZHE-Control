import json, urllib.request, os, sys

KEY = "cdad444bdffca29d788168f6f061bf87"
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "posters")
os.makedirs(OUT, exist_ok=True)

# name -> steam appid (or search term for non-steam)
GAMES = [
    ("genshin",   None,   "genshin impact"),
    ("eldenring", 1245620, None),
    ("bg3",       1086940, None),
    ("cyberpunk", 1091500, None),
    ("wukong",    2358720, None),
    ("sekiro",    814380,  None),
    ("starfield", 1716740, None),
    ("hogwarts",  990080,  None),
    ("rdr2",      1174180, None),
]

def api(url):
    req = urllib.request.Request(url, headers={"Authorization": "Bearer " + KEY, "User-Agent": "proto"})
    with urllib.request.urlopen(req, timeout=30) as r:
        return json.loads(r.read().decode())

def pick(data):
    # prefer clean backgrounds: no_logo > white_logo > others; 600x900 first
    order = {"no_logo": 0, "white_logo": 1, "alternate": 2, None: 3}
    def score(g):
        s = order.get(g.get("style"), 4)
        if g.get("width") == 600 and g.get("height") == 900:
            s -= 0.5
        return s
    return sorted(data, key=score)[0]

def grids_for(name, appid, term):
    if appid:
        j = api("https://www.steamgriddb.com/api/v2/grids/steam/%d" % appid)
        if j.get("success") and j.get("data"):
            return j["data"]
    # fallback / non-steam: search then grids/game/{id}
    if term:
        s = api("https://www.steamgriddb.com/api/v2/search/" + urllib.parse.quote(term))
        if s.get("success") and s.get("data"):
            gid = s["data"][0]["id"]
            g = api("https://www.steamgriddb.com/api/v2/grids/game/%d" % gid)
            if g.get("success") and g.get("data"):
                return g["data"]
    return []

import urllib.parse
for name, appid, term in GAMES:
    try:
        data = grids_for(name, appid, term)
        if not data:
            print("NO DATA", name); continue
        g = pick(data)
        url = g["url"]
        ext = "png" if "png" in g.get("mime", "") else "jpg"
        fn = os.path.join(OUT, name + "." + ext)
        urllib.request.urlretrieve(url, fn)
        print("OK", name, g.get("style"), g.get("width"), "x", g.get("height"), "->", fn, os.path.getsize(fn), "bytes")
    except Exception as e:
        print("ERR", name, e)
