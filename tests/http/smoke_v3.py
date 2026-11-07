"""
Uji asap + ketahanan transaksi V3 lewat HTTP NYATA terhadap aplikasi yang sedang berjalan.

Cara pakai (aplikasi harus sudah berjalan, mis. `dotnet run --project src/Platform.Web --urls http://127.0.0.1:5088`):
    python3 tests/http/smoke_v3.py [http://127.0.0.1:5088] [jalur-file-database.db]

Skrip hanya memakai pustaka standar Python. Database dibuka READ-ONLY untuk memeriksa invarian (stok, pembayaran, penjualan).
"""
import concurrent.futures, os, re, sqlite3, sys, threading

sys.path.insert(0, os.path.dirname(__file__))
import http_client as H

BASE = sys.argv[1] if len(sys.argv) > 1 else "http://127.0.0.1:5088"
DB = sys.argv[2] if len(sys.argv) > 2 else os.path.join(os.path.dirname(__file__), "..", "..", "src", "Platform.Web", "App_Data", "platform-demo-v3.db")
H.BASE = BASE
R = []

def db():
    con = sqlite3.connect(f"file:{DB}?mode=ro", uri=True)
    con.row_factory = sqlite3.Row
    return con

def q(sql, *a):
    with db() as con:
        return con.execute(sql, a).fetchall()

def new(email):
    c = H.Client(BASE)
    code, final = c.login(email)
    assert "/Account/Login" not in final, f"login gagal untuk {email}"
    return c

def shop_id(client, name):
    _, body, _, _, _ = client.get("/Shop")
    m = re.search(r'href="/Shop/Detail/(\d+)"[^>]*>\s*<div class="row-between"><b>' + re.escape(name), body)
    assert m, "warung tidak ditemukan di katalog: " + name
    return int(m.group(1))

def product_field(detail_html, product):
    m = re.search(r'name="Qty\[(\d+)\]"[^>]*aria-label="Jumlah ' + re.escape(product) + '"', detail_html)
    assert m, "produk tidak ada di halaman toko: " + product
    return int(m.group(1))

def review_and_place(client, sid, pid, qty, method="DemoSimulation", key=None):
    fields = {"SellerBusinessId": sid, "Fulfillment": "Pickup", "PaymentMethod": method, "Note": "uji", f"Qty[{pid}]": qty}
    code, body, url, ms, _ = client.post("/Shop/Review", fields, f"/Shop/Detail/{sid}")
    m = re.search(r'name="IdempotencyKey"[^>]*value="([0-9a-f]+)"', body)
    if not m:
        return code, body, None, None
    key = key or m.group(1)
    fields["IdempotencyKey"] = key
    return client.post("/Shop/Place", fields, f"/Shop/Detail/{sid}") + (key,)

print("== 1. Checkout dikirim dua kali dengan kunci sama (klik ganda / kirim ulang)")
buyer = new("pembeli@demo.test")
sid = shop_id(buyer, "Warung Maju")
_, detail, _, _, _ = buyer.get(f"/Shop/Detail/{sid}")
pid = product_field(detail, "Kopi Sachet")
before = q("SELECT COUNT(*) n FROM ConsumerOrders")[0]["n"]
r1 = review_and_place(buyer, sid, pid, 2)
code1, body1, url1, ms1, _h, key = r1
r2 = review_and_place(buyer, sid, pid, 2, key=key)
url2 = r2[2]
after = q("SELECT COUNT(*) n FROM ConsumerOrders")[0]["n"]
H.ok(re.search(r"/ConsumerOrders/Details/(\d+)", url1) is not None, f"Place berhasil dan dialihkan ke detail pesanan ({url1})", R)
H.ok(url1 == url2, "pengiriman ulang dengan kunci sama menuju pesanan YANG SAMA", R)
H.ok(after == before + 1, f"hanya satu pesanan baru tercatat ({before} -> {after})", R)
order_id = int(re.search(r"/Details/(\d+)", url1).group(1))

print("== 2. Pembayaran simulasi dari dua tab (kunci berbeda) dan refresh")
_, det, _, _, _ = buyer.get(f"/ConsumerOrders/Details/{order_id}")
H.ok("SIMULASI / DEMO" in det, "halaman pembayaran berlabel SIMULASI / DEMO", R)
H.ok("Bayar (simulasi)" in det, "tombol bayar simulasi tersedia", R)
k1 = re.search(r'name="key" value="([0-9a-f]+)"', det).group(1)
p1 = buyer.post(f"/ConsumerOrders/Pay", {"id": order_id, "key": k1, "simulateFailure": "false"}, f"/ConsumerOrders/Details/{order_id}")
p2 = buyer.post(f"/ConsumerOrders/Pay", {"id": order_id, "key": "tab-lain-" + k1, "simulateFailure": "false"}, f"/ConsumerOrders/Details/{order_id}")
p3 = buyer.post(f"/ConsumerOrders/Pay", {"id": order_id, "key": k1, "simulateFailure": "false"}, f"/ConsumerOrders/Details/{order_id}")  # refresh/ulang
succ = q("SELECT COUNT(*) n FROM Payments WHERE ConsumerOrderId=? AND Status='Success'", order_id)[0]["n"]
total = q("SELECT COUNT(*) n FROM Payments WHERE ConsumerOrderId=?", order_id)[0]["n"]
H.ok(succ == 1 and total == 1, f"tepat satu catatan pembayaran, berstatus Success (sukses={succ}, total={total})", R)
H.ok(q("SELECT PaymentStatus s FROM ConsumerOrders WHERE Id=?", order_id)[0]["s"] == "Paid", "status pembayaran pesanan = Paid", R)
_, det, _, _, _ = buyer.get(f"/ConsumerOrders/Details/{order_id}")
H.ok(len(re.findall(r"<code>PAY-\d+</code>", det)) == 1, "halaman menampilkan satu nomor referensi pembayaran", R)

print("== 3. Penjual memproses; penerimaan dikonfirmasi berulang tidak menggandakan penjualan/stok")
shop = new("warung@demo.test")
for action in ["Accept", "Prepare", "MarkReady", "HandOver"]:
    shop.post("/ConsumerOrders/Act", {"id": order_id, "action": action}, f"/ConsumerOrders/Details/{order_id}")
st = q("SELECT Status s FROM ConsumerOrders WHERE Id=?", order_id)[0]["s"]
H.ok(st == "PickedUp", f"status setelah penjual menyerahkan = PickedUp ({st})", R)
for _ in range(3):
    buyer.post("/ConsumerOrders/Act", {"id": order_id, "action": "ConfirmReceived"}, f"/ConsumerOrders/Details/{order_id}")
H.ok(q("SELECT Status s FROM ConsumerOrders WHERE Id=?", order_id)[0]["s"] == "Completed", "pesanan selesai", R)
H.ok(q("SELECT COUNT(*) n FROM Sales WHERE SourceType='CONSUMER_ORDER' AND SourceId=?", order_id)[0]["n"] == 1, "penjualan (omzet) tercatat tepat SEKALI", R)
H.ok(q("SELECT COUNT(*) n FROM ConsumerOrderEvents WHERE ConsumerOrderId=? AND Status='Completed'", order_id)[0]["n"] == 1, "event Completed hanya satu", R)

print("== 4. Dua pembeli (dua sesi) berebut stok serentak lewat HTTP")
b1, b2 = new("pembeli@demo.test"), new("pembeli2@demo.test")
_, d, _, _, _ = b1.get(f"/Shop/Detail/{sid}")
pid2 = product_field(d, "Teh Botol 350ml")
avail = int(re.search(r'max="(\d+)"[^>]*name="Qty\[%d\]"' % pid2, d).group(1))
want = avail // 2 + 1  # dua permintaan masing-masing > setengah stok: maksimal satu boleh berhasil
barrier = threading.Barrier(2)
def go(c):
    fields = {"SellerBusinessId": sid, "Fulfillment": "Pickup", "PaymentMethod": "CashOnPickup", f"Qty[{pid2}]": want}
    _, body, _, _, _ = c.post("/Shop/Review", fields, f"/Shop/Detail/{sid}")
    fields["IdempotencyKey"] = re.search(r'name="IdempotencyKey"[^>]*value="([0-9a-f]+)"', body).group(1)
    barrier.wait()
    return c.post("/Shop/Place", fields, f"/Shop/Detail/{sid}")
with concurrent.futures.ThreadPoolExecutor(2) as ex:
    res = list(ex.map(go, [b1, b2]))
wins = sum(1 for r in res if "/ConsumerOrders/Details/" in r[2])
H.ok(wins == 1, f"tepat satu dari dua permintaan serentak berhasil (stok {avail}, masing-masing minta {want}); berhasil={wins}", R)
inv = q("SELECT i.QtyBase q, i.ReservedBase r FROM Inventories i JOIN Products p ON p.Id=i.ProductId JOIN Businesses b ON b.Id=i.BusinessId WHERE p.Name='Teh Botol 350ml' AND b.Name='Warung Maju'")[0]
H.ok(inv["q"] >= 0 and 0 <= inv["r"] <= inv["q"], f"stok tidak minus dan reservasi dalam rentang (qty={inv['q']}, ditahan={inv['r']})", R)

print("== 5. Invarian stok SELURUH database (rekonsiliasi langsung di SQL)")
bad = q("""SELECT i.Id FROM Inventories i
           WHERE i.QtyBase < 0 OR i.ReservedBase < 0 OR i.ReservedBase > i.QtyBase
              OR i.QtyBase <> COALESCE((SELECT SUM(QtyBaseSigned) FROM InventoryMovements m WHERE m.InventoryId=i.Id),0)
              OR i.ReservedBase <> COALESCE((SELECT SUM(QtyBase) FROM StockReservations r WHERE r.InventoryId=i.Id AND r.Status='Active'),0)""")
H.ok(len(bad) == 0, f"tidak ada pelanggaran invarian stok/movement/reservasi ({len(bad)} pelanggaran)", R)
badb = q("""SELECT b.Id FROM StockBatches b WHERE b.QtyRemainingBase < 0 OR b.ReservedBase < 0 OR b.ReservedBase > b.QtyRemainingBase
            OR b.ReservedBase <> COALESCE((SELECT SUM(QtyBase) FROM StockReservations r WHERE r.BatchId=b.Id AND r.Status='Active'),0)""")
H.ok(len(badb) == 0, f"tidak ada pelanggaran invarian batch ({len(badb)} pelanggaran)", R)
dup = q("SELECT ConsumerOrderId FROM Payments WHERE Status='Success' GROUP BY ConsumerOrderId HAVING COUNT(*)>1")
H.ok(len(dup) == 0, "tidak ada pesanan dengan dua pembayaran sukses", R)

print("== 6. Hak akses lintas peran")
cons = new("pembeli@demo.test")
for path in ["/Stock", "/Products", "/Orders", "/Admin/Businesses", "/Delivery"]:
    c, b, u, ms, _ = cons.get(path)
    denied = "/status/403" in u and ("ditolak" in b.lower() or "tidak memiliki akses" in b.lower() or "403" in b)
    H.ok(denied or c in (403, 404), f"pembeli ditolak di {path} (dialihkan ke {u.replace(BASE,'')})", R)
anon = H.Client(BASE)
for path in ["/Shop", "/Stock", "/Orders", "/ConsumerOrders"]:
    c, b, u, ms, _ = anon.get(path)
    H.ok("/Account/Login" in u, f"anonim dialihkan ke login di {path}", R)

print("== 7. Permintaan yang dimanipulasi dan endpoint sensitif")
import urllib.error, urllib.request, urllib.parse
def raw_post(client, path, data):
    req = urllib.request.Request(BASE + path, data=urllib.parse.urlencode(data).encode(), method="POST")
    try:
        r = client.opener.open(req, timeout=30); return r.getcode(), r.geturl()
    except urllib.error.HTTPError as e:
        return e.code, e.geturl()
victim = new("pembeli2@demo.test")
_, d2, _, _, _ = victim.get(f"/Shop/Detail/{sid}")
pid3 = product_field(d2, "Kopi Sachet")
v_order = review_and_place(victim, sid, pid3, 1, method="DemoSimulation")[2]
v_id = int(re.search(r"/Details/(\d+)", v_order).group(1))
attacker = new("pembeli@demo.test")
code, _ = raw_post(attacker, "/ConsumerOrders/Pay", {"id": v_id, "key": "x", "simulateFailure": "false"})
H.ok(code == 400, f"POST tanpa token antiforgery ditolak (HTTP {code})", R)
attacker.post("/ConsumerOrders/Pay", {"id": v_id, "key": "peretas", "simulateFailure": "false"}, f"/ConsumerOrders/Details/{order_id}")
H.ok(q("SELECT COUNT(*) n FROM Payments WHERE ConsumerOrderId=? AND Status='Success'", v_id)[0]["n"] == 0, "pembeli lain TIDAK dapat membayar pesanan orang lain", R)
attacker.post("/ConsumerOrders/Act", {"id": v_id, "action": "BuyerCancel"}, f"/ConsumerOrders/Details/{order_id}")
H.ok(q("SELECT Status s FROM ConsumerOrders WHERE Id=?", v_id)[0]["s"] == "Pending", "pembeli lain TIDAK dapat membatalkan pesanan orang lain", R)
c, b, u, ms, _ = attacker.get(f"/ConsumerOrders/Details/{v_id}")
H.ok(c == 404, f"pembeli lain mendapat 404 saat membuka detail pesanan orang lain (HTTP {c})", R)
c, b, u, ms, _ = shop.get(f"/ConsumerOrders/Details/{order_id}")
H.ok(c == 200 and "uji" in b.lower() or c == 200, "warung penjual dapat membuka pesanan miliknya", R)
other_shop = new("warung2@demo.test")
c, b, u, ms, _ = other_shop.get(f"/ConsumerOrders/Details/{order_id}")
H.ok(c == 404, f"warung lain mendapat 404 untuk pesanan pembeli milik warung berbeda (HTTP {c})", R)
code, final = raw_post(H.Client(BASE), "/Shop/Place", {"SellerBusinessId": sid})
H.ok("/Account/Login" in final or code in (302, 400, 401, 403), f"POST anonim ke /Shop/Place ditolak ({code})", R)
# kata sandi/ID di halaman error tidak membocorkan stack trace
c, b, u, ms, _ = attacker.get("/ConsumerOrders/Details/99999999")
H.ok("StackTrace" not in b and "at Platform.Web" not in b, "halaman galat tidak menampilkan stack trace", R)

print("== 8. Halaman yang dirender tidak memuat <script>/<link>/<img> eksternal (tile peta OpenStreetMap dimuat lewat JS, bukan HTML)")
import html as _html
hosts_found = set()
pages = {"pembeli@demo.test": ["/", "/Shop", "/Shop/Promos", f"/Shop/Detail/{sid}", "/ConsumerOrders", "/Consumer/Profile"],
         "warung@demo.test": ["/", "/ConsumerOrders", "/ConsumerPromos", "/Marketplace", "/Map", "/Sales/Pos"],
         "grosir@demo.test": ["/", "/Marketplace", "/Orders", "/Map"],
         "ternak@demo.test": ["/", "/Chain/Dashboard", "/Chain/Produksi", "/Chain/Stok", "/Chain/Rencana", "/Products"],
         "ternak2@demo.test": ["/", "/Chain/Dashboard", "/Chain/Produksi", "/Chain/Stok", "/Chain/Rencana", "/Products"],
         "admin@demo.test": ["/", "/Admin/Businesses"]}
count = 0
for email, paths in pages.items():
    cl = new(email)
    for path in paths:
        c, b, u, ms, _ = cl.get(path)
        if c != 200:
            continue
        count += 1
        for m in re.finditer(r'<(?:script|link|img|iframe|source)[^>]+(?:src|href)="(https?:)?//([^/"]+)', b):
            hosts_found.add(m.group(2))
H.ok(count >= 25, f"{count} halaman diperiksa", R)
H.ok(len(hosts_found) == 0, f"tidak ada <script>/<link>/<img> ke host eksternal (ditemukan: {sorted(hosts_found) or 'tidak ada'})", R)
c, b, u, ms, _ = new("warung@demo.test").get("/Map")
H.ok("online: true" in b, "peta memakai tile OpenStreetMap (PLATFORM_MAP.online = true) dengan kisi lokal sebagai cadangan", R)

print("== 9. Waktu respons HTTP halaman katalog (target NFR: maksimal 3 detik)")
import statistics, time
def timing(client, path, n=20):
    client.get(path)  # pemanasan
    xs = []
    for _ in range(n):
        xs.append(client.get(path)[3])
    xs.sort()
    return statistics.median(xs), xs[int(len(xs) * 0.95) - 1], xs[-1]
perf_lines = []
for who, path, label in [(w, "/Products", "Katalog produk warung"), (w, "/Marketplace", "Marketplace grosir"),
                        (buyer, "/Shop", "Katalog pembeli: daftar warung"), (buyer, f"/Shop/Detail/{sid}", "Katalog pembeli: toko")]:
    med, p95, mx = timing(who, path)
    line = f"{label} ({path.split('?')[0]}): median {med:.0f} ms, p95 {p95:.0f} ms, maks {mx:.0f} ms (20 permintaan)"
    perf_lines.append(line); print("  " + line)
    H.ok(mx <= 3000, f"{label} <= 3000 ms (maks {mx:.0f} ms)", R)
open(os.path.join(os.path.dirname(__file__), "hasil-waktu-respons.txt"), "w").write("\n".join(perf_lines) + "\n")

passed = sum(1 for r in R if r[0])
print(f"\nRINGKASAN: {passed}/{len(R)} pemeriksaan lulus")
sys.exit(0 if passed == len(R) else 1)
