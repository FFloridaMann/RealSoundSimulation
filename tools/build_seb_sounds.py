"""Build the Sound Engine Backport (SEB) gun sounds for Real Sound Simulation's "0 Gun sounds" switch.

SEB ships EFT 1.0 gun sound banks as bundles with the SAME internal names (CAB) as SPT's own, so Unity
can't load them next to SPT's. This copies SEB's gun banks (and every SEB bundle they reference),
gives each a new internal name, rewrites the references between them, and writes an index the
plugin reads. References to bundles SEB doesn't ship are left alone: they resolve to SPT's own,
which the game has loaded already (e.g. the distance blend options).

Read-only on the input. Writes only to --out (default: the plugin's seb_sounds folder).

    python -m venv .venv
    .venv\\Scripts\\pip install UnityPy
    .venv\\Scripts\\python -I tools\\build_seb_sounds.py [--seb <zip or folder>] [--out <folder>]
"""
import argparse
import hashlib
import io
import os
import shutil
import sys
import tempfile
import zipfile

import UnityPy

AUDIO = "SPT_Runtime/user/mods/SoundEngineBackport/bundles/assets/content/audio/"
# SEB bundles that may hold gun banks or what they reference. Ambience, rain and the plugin-only
# live_embedded banks are never used.
CANDIDATE_DIRS = ("banks/", "weapons/", "blendoptions/", "prefabs/")
SKIP_PREFIXES = ("banks/live_embedded/", "weapons/usable_items/")
TAG = "rss-seb:"


def log(msg):
    print(msg, flush=True)


def new_cab(old):
    return "CAB-" + hashlib.md5((TAG + old).encode()).hexdigest()


def main_cab(env):
    names = [k for k in env.file.files if not k.endswith((".resource", ".resS"))]
    if len(names) != 1:
        raise RuntimeError(f"expected one serialized file, found {names}")
    return names[0]


def cab_of_external(path):
    # "archive:/CAB-x/CAB-x" -> "CAB-x"
    return path.rstrip("/").split("/")[-1]


def extract(seb, tmp):
    """Return {key: path} for candidate SEB bundles; key is relative to content/audio/."""
    out = {}
    if os.path.isdir(seb):
        root = os.path.join(seb, *AUDIO.split("/"))
        for dp, _, files in os.walk(root):
            for f in files:
                if f.endswith(".bundle"):
                    key = os.path.relpath(os.path.join(dp, f), root).replace("\\", "/")
                    out[key] = os.path.join(dp, f)
    else:
        with zipfile.ZipFile(seb) as z:
            for n in z.namelist():
                if n.startswith(AUDIO) and n.endswith(".bundle"):
                    key = n[len(AUDIO):]
                    if key.startswith(CANDIDATE_DIRS) and not key.startswith(SKIP_PREFIXES):
                        dst = os.path.join(tmp, *key.split("/"))
                        os.makedirs(os.path.dirname(dst), exist_ok=True)
                        with z.open(n) as src, open(dst, "wb") as f:
                            shutil.copyfileobj(src, f)
                        out[key] = dst
    return {k: v for k, v in out.items() if k.startswith(CANDIDATE_DIRS) and not k.startswith(SKIP_PREFIXES)}


def flat(key):
    return key.replace("/", "__").replace(" ", "_")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--seb", default=os.path.join(os.path.expanduser("~"), "Downloads", "SPT-SEB-1.0.1.zip"))
    ap.add_argument("--out", default=r"G:\SPT\BepInEx\plugins\FFloridaMann-RealSoundSimulation\seb_sounds")
    a = ap.parse_args()

    tmp = tempfile.mkdtemp(prefix="seb_sounds_")
    try:
        log(f"reading {a.seb}")
        files = extract(a.seb, tmp)
        log(f"{len(files)} candidate SEB bundles")

        # 1. internal name, references and banks of every candidate
        info = {}
        for key, path in sorted(files.items()):
            env = UnityPy.load(path)
            cab = main_cab(env)
            sf = env.file.files[cab]
            exts = [cab_of_external(e.path) for e in sf.externals]
            banks = []
            for o in env.objects:
                if o.type.name == "MonoBehaviour":
                    t = o.read_typetree()
                    if "Environments" in t and "BlendOptions" in t:
                        banks.append(t["m_Name"])
            info[key] = dict(path=path, cab=cab, exts=exts, banks=banks)
        by_cab = {v["cab"].lower(): k for k, v in info.items()}

        # 2. bank bundles and everything SEB ships that they reference (transitively)
        roots = [k for k, v in info.items() if k.startswith("banks/") and v["banks"]]
        needed, order = set(), []

        def visit(k, stack=()):
            if k in needed:
                return
            if k in stack:
                raise RuntimeError(f"reference loop: {stack + (k,)}")
            for c in info[k]["exts"]:
                dep = by_cab.get(c.lower())
                if dep:
                    visit(dep, stack + (k,))
            needed.add(k)
            order.append(k)  # dependencies before dependants

        for r in sorted(roots):
            visit(r)

        def deps_of(k):
            seen, out = set(), []

            def go(x):
                for c in info[x]["exts"]:
                    d = by_cab.get(c.lower())
                    if d and d not in seen:
                        seen.add(d)
                        go(d)
                        out.append(d)
            go(k)
            return out

        rename = {info[k]["cab"]: new_cab(info[k]["cab"]) for k in needed}
        rename_lower = {o.lower(): n.lower() for o, n in rename.items()}
        external_spt = sorted({c for k in needed for c in info[k]["exts"] if c.lower() not in by_cab})

        # 3. write renamed copies
        os.makedirs(a.out, exist_ok=True)
        for f in os.listdir(a.out):
            if f.endswith(".bundle") or f == "index.txt":
                os.remove(os.path.join(a.out, f))
        for k in order:
            env = UnityPy.load(info[k]["path"])
            bf = env.file
            for o in env.objects:
                if o.type.name == "AudioClip":
                    t = o.read_typetree()
                    src = t["m_Resource"]["m_Source"]
                    for old, new in rename.items():
                        src = src.replace(old, new)
                    t["m_Resource"]["m_Source"] = src
                    o.save_typetree(t)
                elif o.type.name == "AssetBundle":
                    t = o.read_typetree()
                    t["m_Name"] = t["m_AssetBundleName"] = "rss/seb/" + k
                    t["m_Dependencies"] = [rename_lower.get(d.lower(), d) for d in t.get("m_Dependencies", [])]
                    o.save_typetree(t)
            sf = bf.files[info[k]["cab"]]
            for e in sf.externals:
                c = cab_of_external(e.path)
                if c in rename:
                    e.path = e.path.replace(c, rename[c])
            renamed = {}
            for name, f in bf.files.items():
                nn = name
                for old, new in rename.items():
                    nn = nn.replace(old, new)
                f.name = nn
                renamed[nn] = f
            bf.files = renamed
            with open(os.path.join(a.out, flat(k)), "wb") as fh:
                fh.write(bf.save(packer="lz4"))

        # 4. index for the plugin
        lines = ["# Real Sound Simulation SEB gun sounds. Built by tools/build_seb_sounds.py; do not edit.",
                 "# bundle <file> <dependency files in load order, comma separated or ->",
                 "# bank <bank name> <file>"]
        bank_home = {}
        for k in order:
            d = deps_of(k)
            lines.append(f"bundle\t{flat(k)}\t{','.join(flat(x) for x in d) or '-'}")
        dup = []
        for k in sorted(roots):
            for b in info[k]["banks"]:
                if b in bank_home:
                    dup.append(b)
                    continue
                bank_home[b] = k
                lines.append(f"bank\t{b}\t{flat(k)}")
        with open(os.path.join(a.out, "index.txt"), "w", encoding="utf-8", newline="\n") as fh:
            fh.write("\n".join(lines) + "\n")

        # 5. check: re-open every output, internal names renamed, no reference to an old SEB name,
        #    one clip decodes per bundle that has clips
        problems = []
        clips_total = 0
        old_lower = set(rename_lower)
        for k in order:
            p = os.path.join(a.out, flat(k))
            env = UnityPy.load(p)
            cab = main_cab(env)
            if cab != rename[info[k]["cab"]]:
                problems.append(f"{k}: internal name {cab} not renamed")
            for e in env.file.files[cab].externals:
                if cab_of_external(e.path).lower() in old_lower:
                    problems.append(f"{k}: still references {e.path}")
            decoded = False
            for o in env.objects:
                if o.type.name == "AudioClip":
                    clips_total += 1
                    c = o.read()
                    if any(old in c.m_Resource.m_Source for old in rename):
                        problems.append(f"{k}: clip {c.m_Name} still points at {c.m_Resource.m_Source}")
                    if not decoded:
                        if not next(iter(c.samples.values()), b""):
                            problems.append(f"{k}: clip {c.m_Name} did not decode")
                        decoded = True

        log(f"wrote {len(order)} bundles ({len(roots)} with banks), {len(bank_home)} banks, {clips_total} clips to {a.out}")
        log(f"references left to SPT's own bundles: {len(external_spt)} ({', '.join(external_spt[:6])}{' ...' if len(external_spt) > 6 else ''})")
        if dup:
            log(f"warning: bank names in more than one bundle, first one kept: {', '.join(sorted(set(dup)))}")
        if problems:
            log("PROBLEMS:")
            for p in problems:
                log("  " + p)
            return 1
        log("check passed")
        return 0
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


if __name__ == "__main__":
    sys.exit(main())
