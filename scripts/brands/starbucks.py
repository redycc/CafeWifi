#!/usr/bin/env python3
"""Fetch every Starbucks Taiwan store that offers Wi-Fi and write data/brands/starbucks.json.

Starbucks Wi-Fi is an open network ("Starbucks Wi-Fi") behind a terms page, the same in
every store, so there are no passwords: the stores are only used to tell, by location,
which one you're in.
"""
import html
import json
import re
import sys
import urllib.parse
import urllib.request
from pathlib import Path

BRAND = {"id": "starbucks", "name": "星巴克", "ssids": ["Starbucks Wi-Fi"], "security": "open"}
URL = "https://www.starbucks.com.tw/stores/ajax/json_storesearch.aspx"
OUT = Path(__file__).resolve().parents[2] / "data" / "brands" / "starbucks.json"

STORE = re.compile(
    r"fetchStoreMapLocation\((\d+),\s*([-\d.]+),\s*([-\d.]+)\).*?"
    r"<h4 class='store_name'>(.*?)</h4>\s*<p class='store_add'>(.*?)</p>",
    re.S)


def fetch():
    # The site's own search form: all stores sorted by distance from a point; wifi=1 keeps Wi-Fi stores.
    body = urllib.parse.urlencode({"q": "", "searchStoreName": "", "lat": "25.0401998", "lon": "121.5634194",
                                   "wifi": "1"}).encode()
    req = urllib.request.Request(URL, data=body, headers={"User-Agent": "cafe-wifi",
                                                          "Content-Type": "application/x-www-form-urlencoded"})
    with urllib.request.urlopen(req, timeout=60) as r:
        data = json.loads(r.read().decode("utf-8"))
    return next(e["elementHTML"] for e in data["renderElements"] if e["elementID"] == "search_store")


def parse(fragment):
    for _id, lat, lng, name, address in STORE.findall(fragment):
        yield {"name": html.unescape(name).strip(), "address": html.unescape(address).strip(),
               "lat": float(lat), "lng": float(lng)}


def city(address):
    m = re.match(r"(.{2}[縣市])", address.replace("臺", "台"))
    return m.group(1) if m else "其他"


def build(raw):
    stores, seen = [], set()
    for s in sorted(raw, key=lambda s: s["name"]):
        if s["name"] in seen:
            continue
        seen.add(s["name"])
        lat, lng = s["lat"], s["lng"]
        if not (21 < lat < 26.5 and 118 < lng < 122.5):
            lat = lng = 0.0  # unknown; still searchable by name
        stores.append({"name": s["name"], "address": s["address"], "city": city(s["address"]), "phone": "",
                       "lat": round(lat, 6), "lng": round(lng, 6), "passwords": []})
    return {**BRAND, "stores": stores}


def main():
    brand = build(parse(fetch()))
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(json.dumps(brand, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
    missing = sum(1 for s in brand["stores"] if not s["lat"])
    print(f"wrote {len(brand['stores'])} stores to {OUT} ({missing} without coordinates)", file=sys.stderr)


if __name__ == "__main__":
    main()
