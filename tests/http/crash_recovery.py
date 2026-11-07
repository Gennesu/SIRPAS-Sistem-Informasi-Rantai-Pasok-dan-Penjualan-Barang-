"""
Uji pemulihan crash: aplikasi dimatikan PAKSA (SIGKILL) di tengah banjir pesanan, lalu database diperiksa.

Fase 1 (beban + kill):   python3 tests/http/crash_recovery.py --load <PID-proses-aplikasi> [url] [file.db]
Fase 2 (jalankan ulang aplikasi secara normal, lalu):   python3 tests/http/crash_recovery.py --verify [url] [file.db]

Yang diperiksa: PRAGMA integrity_check = ok; tidak ada stok minus/reservasi berlebih; saldo stok = jumlah movement;
reservasi aktif = Reserved; tidak ada pesanan tanpa reservasi/pembayaran (tidak ada transaksi setengah jadi);
aplikasi dapat menerima pesanan baru setelah pulih.
"""
import os, re, signal, sqlite3, sys, threading, time
sys.path.insert(0, os.path.dirname(__file__))
import http_client as H

args = [a for a in sys.argv[1:] if not a.startswith("--")]
mode = "--verify" if "--verify" in sys.argv else "--load"
pid = int(args[0]) if mode == "--load" else None
url = (args[1] if mode == "--load" and len(args) > 1 else args[0] if mode == "--verify" and args else "http://127.0.0.1:5088")
db_path = (args[2] if mode == "--load" and len(args) > 2 else args[1] if mode == "--verify" and len(args) > 1
           else os.path.join(os.path.dirname(__file__), "..", "..", "src", "Platform.Web", "App_Data", "platform-demo-v3.db"))
H.BASE = url

def q(sql, *a):
    con = sqlite3.connect(f"file:{db_path}?mode=ro", uri=True); con.row_factory = sqlite3.Row
    try: return con.execute(sql, a).fetchall()
    finally: con.close()

def shop_and_product(c, shop_name, product):
    _, body, *_ = c.get("/Shop")
    sid = int(re.search(r'href="/Shop/Detail/(\d+)"[^>]*>\s*<div class="row-between"><b>' + re.escape(shop_name), body).group(1))
    _, d, *_ = c.get(f"/Shop/Detail/{sid}")
    pid = int(re.search(r'name="Qty\[(\d+)\]"[^>]*aria-label="Jumlah ' + re.escape(product) + '"', d).group(1))
    return sid, pid

def any_available_product(c, sid):
    """Produk dengan stok tersedia terbanyak di toko (stok produk beban bisa saja sudah habis)."""
    _, d, *_ = c.get(f"/Shop/Detail/{sid}")
    found = [(int(m.group(1)), int(m.group(2))) for m in re.finditer(r'max="(\d+)"[^>]*name="Qty\[(\d+)\]"', d)]
    found = [(int(p), int(mx)) for mx, p in [(a, b) for a, b in [(f[0], f[1]) for f in found]] if int(mx) > 0]
    assert found, "tidak ada produk tersedia di toko"
    return max(found, key=lambda x: x[1])[0]

def place(c, sid, pid, qty, key):
    f = {"SellerBusinessId": sid, "Fulfillment": "Pickup", "PaymentMethod": "DemoSimulation", f"Qty[{pid}]": qty, "IdempotencyKey": key}
    return c.post("/Shop/Place", f, f"/Shop/Detail/{sid}")

if mode == "--load":
    accounts = ["pembeli@demo.test", "pembeli2@demo.test"]
    sessions = []
    for i in range(6):
        c = H.Client(url); c.login(accounts[i % 2]); sessions.append(c)
    sid, pid = shop_and_product(sessions[0], "Warung Maju", "Kopi Sachet")
    before = q("SELECT COUNT(*) n FROM ConsumerOrders")[0]["n"]
    stop = threading.Event(); done = [0]
    def worker(c, n):
        i = 0
        while not stop.is_set():
            try:
                place(c, sid, pid, 1, f"crash-{n}-{i}-{time.time_ns()}"); done[0] += 1
            except Exception:
                break
            i += 1
    threads = [threading.Thread(target=worker, args=(c, n), daemon=True) for n, c in enumerate(sessions)]
    for t in threads: t.start()
    time.sleep(4.0)
    print(f"[load] pesanan terkirim sebelum kill: ~{done[0]}; di DB: {q('SELECT COUNT(*) n FROM ConsumerOrders')[0]['n'] - before}")
    os.kill(pid, signal.SIGKILL)  # crash paksa di tengah transaksi
    print(f"[load] proses {pid} dimatikan paksa (SIGKILL) di tengah beban")
    time.sleep(1.0); stop.set()
    sys.exit(0)

# ---- verifikasi
R = []
print("== Verifikasi setelah crash dan restart")
H.ok(q("PRAGMA integrity_check")[0][0] == "ok", "PRAGMA integrity_check = ok", R)
H.ok(q("PRAGMA foreign_key_check") == [], "tidak ada pelanggaran foreign key", R)
bad = q("""SELECT i.Id FROM Inventories i
           WHERE i.QtyBase < 0 OR i.ReservedBase < 0 OR i.ReservedBase > i.QtyBase
              OR i.QtyBase <> COALESCE((SELECT SUM(QtyBaseSigned) FROM InventoryMovements m WHERE m.InventoryId=i.Id),0)
              OR i.ReservedBase <> COALESCE((SELECT SUM(QtyBase) FROM StockReservations r WHERE r.InventoryId=i.Id AND r.Status='Active'),0)""")
H.ok(len(bad) == 0, f"invarian stok/movement/reservasi utuh ({len(bad)} pelanggaran)", R)
badb = q("""SELECT b.Id FROM StockBatches b WHERE b.QtyRemainingBase < 0 OR b.ReservedBase < 0 OR b.ReservedBase > b.QtyRemainingBase
            OR b.ReservedBase <> COALESCE((SELECT SUM(QtyBase) FROM StockReservations r WHERE r.BatchId=b.Id AND r.Status='Active'),0)""")
H.ok(len(badb) == 0, f"invarian batch utuh ({len(badb)} pelanggaran)", R)
orphans = q("""SELECT o.Id FROM ConsumerOrders o WHERE o.Status='Pending'
               AND (NOT EXISTS (SELECT 1 FROM StockReservations r WHERE r.RefType='CORDER' AND r.RefId=o.Id)
                    OR NOT EXISTS (SELECT 1 FROM Payments p WHERE p.ConsumerOrderId=o.Id))""")
H.ok(len(orphans) == 0, f"tidak ada pesanan menunggu tanpa reservasi/catatan pembayaran ({len(orphans)} yatim)", R)
noitems = q("SELECT o.Id FROM ConsumerOrders o WHERE NOT EXISTS (SELECT 1 FROM ConsumerOrderItems i WHERE i.ConsumerOrderId=o.Id)")
H.ok(len(noitems) == 0, f"tidak ada pesanan tanpa item ({len(noitems)})", R)
n = q("SELECT COUNT(*) n FROM ConsumerOrders")[0]["n"]
print(f"   (jumlah pesanan tersimpan setelah crash: {n})")
c = H.Client(url); code, final = c.login("pembeli@demo.test")
H.ok("/Account/Login" not in final, "login berhasil setelah pemulihan", R)
sid, _ = shop_and_product(c, "Warung Maju", "Teh Botol 350ml")
pid = any_available_product(c, sid)
code, body, u, ms, _ = place(c, sid, pid, 1, "setelah-pulih-" + str(time.time_ns()))
H.ok("/ConsumerOrders/Details/" in u, "pesanan baru dapat dibuat setelah pulih", R)
print(f"\nRINGKASAN: {sum(1 for r in R if r[0])}/{len(R)} pemeriksaan lulus")
sys.exit(0 if all(r[0] for r in R) else 1)
