
/* SIRPAS - peralatan peta (Leaflet + OpenStreetMap).
   Dipakai oleh semua halaman peta.
   Bawaan: tile OpenStreetMap dimuat dari internet (Map:OnlineTiles = true) di atas kisi skematis lokal, jadi bila internet putus
   pin dan jarak tetap terbaca. Map:OnlineTiles = false mematikan tile dan memakai kisi lokal saja. Koordinat, jarak, dan estimasi
   rute dihitung lokal; fitur transaksi tidak pernah bergantung pada tile. OSRM dan Nominatim TIDAK dipanggil.
*/
(function () {
  'use strict';

  var GLYPHS = {
    "store": "<path d=\"M15 21v-5a1 1 0 0 0-1-1h-4a1 1 0 0 0-1 1v5\" /> <path d=\"M17.774 10.31a1.12 1.12 0 0 0-1.549 0 2.5 2.5 0 0 1-3.451 0 1.12 1.12 0 0 1-1.548 0 2.5 2.5 0 0 1-3.452 0 1.12 1.12 0 0 1-1.549 0 2.5 2.5 0 0 1-3.77-3.248l2.889-4.184A2 2 0 0 1 7 2h10a2 2 0 0 1 1.653.873l2.895 4.192a2.5 2.5 0 0 1-3.774 3.244\" /> <path d=\"M4 10.95V19a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-8.05\" />",
    "house": "<path d=\"M15 21v-8a1 1 0 0 0-1-1h-4a1 1 0 0 0-1 1v8\" /> <path d=\"M3 10a2 2 0 0 1 .709-1.528l7-6a2 2 0 0 1 2.582 0l7 6A2 2 0 0 1 21 10v9a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z\" />",
    "badge-percent": "<path d=\"M3.85 8.62a4 4 0 0 1 4.78-4.77 4 4 0 0 1 6.74 0 4 4 0 0 1 4.78 4.78 4 4 0 0 1 0 6.74 4 4 0 0 1-4.77 4.78 4 4 0 0 1-6.75 0 4 4 0 0 1-4.78-4.77 4 4 0 0 1 0-6.76Z\" /> <path d=\"m15 9-6 6\" /> <path d=\"M9 9h.01\" /> <path d=\"M15 15h.01\" />",
    "check": "<path d=\"M20 6 9 17l-5-5\" />",
    "x": "<path d=\"M18 6 6 18\" /> <path d=\"m6 6 12 12\" />",
    "warehouse": "<path d=\"M18 21V10a1 1 0 0 0-1-1H7a1 1 0 0 0-1 1v11\" /> <path d=\"M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V8a2 2 0 0 1 1.132-1.803l7.95-3.974a2 2 0 0 1 1.837 0l7.948 3.974A2 2 0 0 1 22 8z\" /> <path d=\"M6 13h12\" /> <path d=\"M6 17h12\" />",
    "truck": "<path d=\"M14 18V6a2 2 0 0 0-2-2H4a2 2 0 0 0-2 2v11a1 1 0 0 0 1 1h2\" /> <path d=\"M15 18H9\" /> <path d=\"M19 18h2a1 1 0 0 0 1-1v-3.65a1 1 0 0 0-.22-.624l-3.48-4.35A1 1 0 0 0 17.52 8H14\" /> <circle cx=\"17\" cy=\"18\" r=\"2\" /> <circle cx=\"7\" cy=\"18\" r=\"2\" />",
    "map-pin": "<path d=\"M20 10c0 4.993-5.539 10.193-7.399 11.799a1 1 0 0 1-1.202 0C9.539 20.193 4 14.993 4 10a8 8 0 0 1 16 0\" /> <circle cx=\"12\" cy=\"10\" r=\"3\" />",
    "users": "<path d=\"M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2\" /> <path d=\"M16 3.128a4 4 0 0 1 0 7.744\" /> <path d=\"M22 21v-2a4 4 0 0 0-3-3.87\" /> <circle cx=\"9\" cy=\"7\" r=\"4\" />",
    "flag": "<path d=\"M4 22V4a1 1 0 0 1 .4-.8A6 6 0 0 1 8 2c3 0 5 2 7.333 2q2 0 3.067-.8A1 1 0 0 1 20 4v10a1 1 0 0 1-.4.8A6 6 0 0 1 16 16c-3 0-5-2-8-2a6 6 0 0 0-4 1.528\" />",
    "user": "<path d=\"M19 21v-2a4 4 0 0 0-4-4H9a4 4 0 0 0-4 4v2\" /> <circle cx=\"12\" cy=\"7\" r=\"4\" />"
  };

  var COLORS = {
    shop: '#a8531b',
    customer: '#a8531b',
    warung: '#5a2c10',
    grosir: '#a8531b',
    'grosir-new': '#a8927f',
    peternakan: '#b9770e',
    origin: '#3b2416',
    promo: '#ffc933',
    prospect: '#a8927f',
    out: '#d3362b',
    dest: '#5a2c10'
  };

  var GLYPH_OF = {
    shop: 'store',
    customer: 'house',
    warung: 'house',
    grosir: 'store',
    'grosir-new': 'store',
    peternakan: 'house',
    origin: 'warehouse',
    promo: 'badge-percent',
    prospect: 'house',
    out: 'x',
    dest: 'house'
  };

  var DEFAULT_CENTER = [-6.9175, 107.6191]; // Bandung

  // Diset oleh _MapScripts.cshtml dari konfigurasi Map:OnlineTiles (bawaan true).
  var ONLINE = window.PLATFORM_MAP ? !!window.PLATFORM_MAP.online : true;

  // Peta skematis lokal: kisi + penanda koordinat. Dipakai saat offline atau bila tile online gagal dimuat.
  var LocalGrid = L.GridLayer.extend({
    createTile: function (coords) {
      var tile = document.createElement('canvas');
      var size = this.getTileSize();
      tile.width = size.x; tile.height = size.y;
      var ctx = tile.getContext('2d');
      ctx.fillStyle = '#eef4ef'; ctx.fillRect(0, 0, size.x, size.y);
      ctx.strokeStyle = '#d3e1d6'; ctx.lineWidth = 1;
      for (var i = 0; i <= size.x; i += 64) { ctx.beginPath(); ctx.moveTo(i + .5, 0); ctx.lineTo(i + .5, size.y); ctx.stroke(); ctx.beginPath(); ctx.moveTo(0, i + .5); ctx.lineTo(size.x, i + .5); ctx.stroke(); }
      ctx.strokeStyle = '#b7cdbb'; ctx.strokeRect(.5, .5, size.x - 1, size.y - 1);
      return tile;
    }
  });

  function offlineNotice(map) {
    var c = L.control({ position: 'bottomleft' });
    c.onAdd = function () {
      var d = L.DomUtil.create('div', 'leaflet-bar');
      d.style.cssText = 'background:#fff;padding:6px 10px;font:600 12px system-ui;max-width:260px;border-radius:8px';
      d.textContent = ONLINE
        ? 'Peta jalan belum bisa dimuat (periksa koneksi internet). Menampilkan kisi lokal; lokasi dan jarak tetap akurat.'
        : 'Mode lokal: peta jalan dimatikan (Map:OnlineTiles = false). Lokasi dan jarak tetap akurat.';
      return d;
    };
    c.addTo(map);
  }

  // Tile OpenStreetMap hanya bila online diizinkan; selain itu kisi lokal.
  function tileLayers() {
    if (!ONLINE) { return new LocalGrid({ maxZoom: 19 }); }
    return L.tileLayer(
      'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
      {
        maxZoom: 19,
        zIndex: 2,
        attribution:
          '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors'
      }
    );
  }

  function createMap(el, opts) {
    opts = opts || {};

    var map = L.map(el, {
      zoomControl: false,
      attributionControl: true,
      scrollWheelZoom: opts.scrollWheelZoom !== false,
      tap: true
    });

    map.setView(opts.center || DEFAULT_CENTER, opts.zoom || 13);

    // Kisi lokal dipasang dulu sebagai lapisan dasar: bila tile OpenStreetMap lambat atau gagal, pin dan jarak tetap terbaca.
    if (ONLINE) { new LocalGrid({ maxZoom: 19, zIndex: 0 }).addTo(map); }
    var base = tileLayers().addTo(map);
    if (!ONLINE) {
      offlineNotice(map);
    } else {
      // Tidak ada satu pun tile yang berhasil dimuat (mis. tanpa internet): beri tahu pengguna; transaksi tidak terpengaruh.
      var loaded = 0, errors = 0, warned = false;
      base.on('tileload', function () { loaded++; });
      base.on('tileerror', function () {
        errors++;
        if (!warned && loaded === 0 && errors >= 6) { warned = true; offlineNotice(map); }
      });
    }

    L.control.zoom({ position: 'bottomright' }).addTo(map);
    map.attributionControl.setPrefix(false);

    return map;
  }

  function pinSvg(kind, label) {
    var color = COLORS[kind] || COLORS.shop;
    var glyph = GLYPHS[GLYPH_OF[kind] || 'store'];
    var stroke = kind === 'promo' ? '#3b2a00' : '#fff';

    var text = label
      ? '<text x="22" y="24" text-anchor="middle" font-family="Plus Jakarta Sans, sans-serif" font-weight="800" font-size="12" fill="' + stroke + '">' + label + '</text>'
      : '';

    return '<svg width="44" height="56" viewBox="0 0 44 56" xmlns="http://www.w3.org/2000/svg">' +
      '<path d="M22 2C11.5 2 3 10.3 3 20.6c0 14.2 19 33.4 19 33.4s19-19.2 19-33.4C41 10.3 32.5 2 22 2z" fill="' + color + '" stroke="#fff" stroke-width="3"/>' +
      (label
        ? text
        : '<g transform="translate(11.2 9.8) scale(.9)" stroke="' + stroke + '" stroke-width="2.2" fill="none" stroke-linecap="round" stroke-linejoin="round">' + glyph + '</g>') +
      '</svg>';
  }

  function pin(latlng, kind, opts) {
    opts = opts || {};

    var icon = L.divIcon({
      html: '<div class="pin">' + pinSvg(kind, opts.label) + '</div>',
      className: 'pin-wrap',
      iconSize: [44, 56],
      iconAnchor: [22, 52],
      popupAnchor: [0, -50]
    });

    return L.marker(latlng, {
      icon: icon,
      riseOnHover: true,
      keyboard: true,
      title: opts.title || '',
      zIndexOffset: opts.z || 0
    });
  }

  function setPinBig(marker, big) {
    var el = marker.getElement && marker.getElement();

    if (el) {
      var p = el.querySelector('.pin');
      if (p) p.classList.toggle('big', !!big);
    }

    marker.setZIndexOffset(big ? 1000 : 0);
  }

  function youMarker(latlng) {
    return L.marker(latlng, {
      icon: L.divIcon({
        html: '<div class="you-dot"></div>',
        className: 'pin-wrap',
        iconSize: [20, 20],
        iconAnchor: [10, 10]
      }),
      interactive: false,
      keyboard: false
    });
  }

  function truckMarker(latlng, still) {
    var html =
      '<div class="truck-mark ' + (still ? 'still' : '') + '">' +
      '<div class="t-ring"></div>' +
      '<div class="t-body">' +
      '<svg width="26" height="26" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
      GLYPHS.truck +
      '</svg></div></div>';

    return L.marker(latlng, {
      icon: L.divIcon({
        html: html,
        className: 'pin-wrap',
        iconSize: [52, 52],
        iconAnchor: [26, 26]
      }),
      interactive: false,
      keyboard: false,
      zIndexOffset: 2000
    });
  }

  function haversine(a, b) {
    var R = 6371;
    var rad = Math.PI / 180;
    var dLat = (b[0] - a[0]) * rad;
    var dLon = (b[1] - a[1]) * rad;

    var h =
      Math.sin(dLat / 2) * Math.sin(dLat / 2) +
      Math.cos(a[0] * rad) * Math.cos(b[0] * rad) *
      Math.sin(dLon / 2) * Math.sin(dLon / 2);

    return 2 * R * Math.asin(Math.min(1, Math.sqrt(h)));
  }

  // Garis melengkung sebagai cadangan jika OSRM tidak tersedia.
  function curve(a, b) {
    var mid = [
      (a[0] + b[0]) / 2,
      (a[1] + b[1]) / 2
    ];

    var dx = b[1] - a[1];
    var dy = b[0] - a[0];

    var ctrl = [
      mid[0] + dx * 0.16,
      mid[1] - dy * 0.16
    ];

    var pts = [];

    for (var i = 0; i <= 48; i++) {
      var t = i / 48;
      var u = 1 - t;

      pts.push([
        u * u * a[0] + 2 * u * t * ctrl[0] + t * t * b[0],
        u * u * a[1] + 2 * u * t * ctrl[1] + t * t * b[1]
      ]);
    }

    return pts;
  }

  function lengthOf(coords) {
    var d = 0;

    for (var i = 1; i < coords.length; i++) {
      d += haversine(coords[i - 1], coords[i]);
    }

    return d;
  }

  // Ambil rute jalan dari OSRM. Jika gagal, gunakan garis cadangan.
  function route(a, b) {
    var fallback = function () {
      var c = curve(a, b);
      var km = lengthOf(c);

      return {
        coords: c,
        km: km,
        minutes: Math.round(km / 25 * 60 + 10),
        source: 'line'
      };
    };

    // V3: estimasi rute SELALU lokal (garis lurus x faktor jalan). Tidak ada layanan routing eksternal; hasilnya diberi label
    // "perkiraan" di antarmuka dan tidak pernah dianggap posisi kendaraan nyata.
    return Promise.resolve(fallback());
  }

  // Titik pada lintasan berdasarkan proporsi 0..1.
  function along(coords, t) {
    t = Math.max(0, Math.min(1, t));

    var total = lengthOf(coords);
    var target = total * t;
    var acc = 0;

    for (var i = 1; i < coords.length; i++) {
      var seg = haversine(coords[i - 1], coords[i]);

      if (acc + seg >= target || i === coords.length - 1) {
        var f = seg === 0
          ? 0
          : Math.max(0, Math.min(1, (target - acc) / seg));

        var p = [
          coords[i - 1][0] + (coords[i][0] - coords[i - 1][0]) * f,
          coords[i - 1][1] + (coords[i][1] - coords[i - 1][1]) * f
        ];

        var bearing =
          Math.atan2(
            coords[i][1] - coords[i - 1][1],
            coords[i][0] - coords[i - 1][0]
          ) * 180 / Math.PI;

        return {
          latlng: p,
          bearing: bearing,
          index: i
        };
      }

      acc += seg;
    }

    return {
      latlng: coords[coords.length - 1],
      bearing: 0,
      index: coords.length - 1
    };
  }

  function sliceAlong(coords, t) {
    var at = along(coords, t);
    return coords.slice(0, at.index).concat([at.latlng]);
  }

  function fmtKm(km) {
    return (km < 10 ? km.toFixed(1) : Math.round(km))
      .toString()
      .replace('.', ',') + ' km';
  }

  window.MapKit = {
    DEFAULT_CENTER: DEFAULT_CENTER,
    createMap: createMap,
    pin: pin,
    setPinBig: setPinBig,
    youMarker: youMarker,
    truckMarker: truckMarker,
    haversine: haversine,
    route: route,
    along: along,
    sliceAlong: sliceAlong,
    fmtKm: fmtKm,
    GLYPHS: GLYPHS
  };
})();
