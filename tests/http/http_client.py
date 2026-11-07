"""Klien HTTP kecil (stdlib) untuk uji asap end-to-end terhadap aplikasi yang BENAR-BENAR berjalan."""
import http.cookiejar, re, sys, time, urllib.request, urllib.parse, urllib.error

BASE = "http://127.0.0.1:5088"

class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *a, **k): return None

class Client:
    def __init__(self, base=BASE, follow=True):
        self.base = base
        self.jar = http.cookiejar.CookieJar()
        handlers = [urllib.request.HTTPCookieProcessor(self.jar)]
        if not follow: handlers.append(NoRedirect())
        self.opener = urllib.request.build_opener(*handlers)
    def _do(self, req):
        t = time.perf_counter()
        try:
            r = self.opener.open(req, timeout=60); body = r.read().decode("utf-8", "replace"); code = r.getcode(); url = r.geturl(); hdr = r.headers
        except urllib.error.HTTPError as e:
            body = e.read().decode("utf-8", "replace"); code = e.code; url = e.geturl(); hdr = e.headers
        return code, body, url, (time.perf_counter() - t) * 1000, hdr
    def get(self, path):
        return self._do(urllib.request.Request(self.base + path))
    def token(self, html):
        m = re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"', html)
        return m.group(1) if m else None
    def post(self, path, data, form_page=None):
        page = form_page or path
        c, body, u, ms, h = self.get(page)
        tok = self.token(body)
        data = dict(data)
        if tok: data["__RequestVerificationToken"] = tok
        enc = urllib.parse.urlencode(data, doseq=True).encode()
        return self._do(urllib.request.Request(self.base + path, data=enc, method="POST"))
    def login(self, email, password="Demo12345"):
        c, body, u, ms, h = self.post("/Account/Login", {"Email": email, "Password": password, "RememberMe": "false"}, "/Account/Login")
        return c, u

def ok(cond, msg, results):
    results.append((bool(cond), msg))
    print(("  PASS " if cond else "  FAIL ") + msg)
    return cond
