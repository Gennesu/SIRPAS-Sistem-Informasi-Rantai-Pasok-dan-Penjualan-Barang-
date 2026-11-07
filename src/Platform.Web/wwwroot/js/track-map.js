/* Pelacakan pesanan: garis rute dari grosir ke warung dengan ikon truk di atasnya.
   Posisi truk DIPERKIRAKAN dari fase pesanan dan waktu berangkat (tanpa GPS, tanpa biaya langganan):
   sebelum berangkat truk diam di toko grosir; setelah berangkat bergerak mengikuti estimasi waktu tempuh;
   berhenti di 95% sampai warung mengonfirmasi barang diterima. Halaman memeriksa perubahan status tiap 20 detik. */
(function () {
  'use strict';
  var el = document.getElementById('track-map');

  // ---- Segarkan otomatis saat status berubah (murah: satu GET kecil)
  var holder = el || document.querySelector('[data-order]');
  var orderId = holder && holder.getAttribute('data-order');
  var currentStatus = holder && holder.getAttribute('data-status');
  if (orderId && currentStatus) {
    setInterval(function () {
      if (document.hidden) return;
      fetch('/Orders/Status/' + orderId, { headers: { Accept: 'application/json' }, credentials: 'same-origin' })
        .then(function (r) { return r.ok ? r.json() : null; })
        .then(function (j) { if (j && j.status && j.status !== currentStatus) { location.reload(); } })
        .catch(function () {});
    }, 20000);
  }

  if (!el || !window.L || !window.MapKit) return;
  var d = el.dataset;
  var seller = [parseFloat(d.sellerLat), parseFloat(d.sellerLon)];
  var buyer = [parseFloat(d.buyerLat), parseFloat(d.buyerLon)];
  var delivery = d.mode === 'Delivery';
  var status = d.status;
  var shipped = d.shipped ? new Date(d.shipped) : null;
  var minutes = parseInt(d.minutes, 10) || null;
  var caption = document.getElementById('track-caption');
  var source = document.getElementById('track-source');

  var map = MapKit.createMap(el, { center: seller, zoom: 13, scrollWheelZoom: false });
  var sellerPin = MapKit.pin(seller, 'origin', { title: d.sellerName }).addTo(map);
  var buyerPin = MapKit.pin(buyer, 'dest', { title: d.buyerName }).addTo(map);
  sellerPin.bindTooltip('<b>' + window.escapeHtml(d.sellerName) + '</b><br>Grosir', { direction: 'top', offset: [0, -50] });
  buyerPin.bindTooltip('<b>' + window.escapeHtml(d.buyerName) + '</b><br>Warung (tujuan)', { direction: 'top', offset: [0, -50] });
  map.fitBounds([seller, buyer], { padding: [60, 60] });

  // Mirror OrderTracker.TruckProgress di server.
  function progress() {
    if (status === 'Completed' || status === 'PickedUp') return 1;
    if (status === 'Delivered') {
      if (!shipped || !minutes) return 0.5;
      var elapsed = (Date.now() - shipped.getTime()) / 60000;
      return Math.max(0.05, Math.min(0.95, elapsed / minutes));
    }
    return 0;
  }

  MapKit.route(seller, buyer).then(function (r) {
    var coords = r.coords;
    L.polyline(coords, { color: '#ffffff', weight: 11, opacity: 0.95, lineCap: 'round', lineJoin: 'round' }).addTo(map);
    var base = L.polyline(coords, { color: '#a9bbb0', weight: 6, opacity: 0.95, lineCap: 'round', lineJoin: 'round', dashArray: delivery ? null : '2 10' }).addTo(map);
    map.fitBounds(base.getBounds(), { padding: [60, 60] });
    if (source) var km = parseFloat(d.km); source.textContent = (r.source === 'osrm' ? 'Rute jalan' : 'Rute perkiraan') + ' · ' + MapKit.fmtKm(isFinite(km) ? km : r.km);
    if (!delivery) return;

    var done = L.polyline([seller], { color: '#a8531b', weight: 6, opacity: 1, lineCap: 'round', lineJoin: 'round' }).addTo(map);
    var truck = MapKit.truckMarker(seller, status !== 'Delivered').addTo(map);
    function render() {
      var p = progress();
      var at = MapKit.along(coords, p);
      truck.setLatLng(at.latlng);
      var node = truck.getElement && truck.getElement();
      if (node) { var m = node.querySelector('.truck-mark'); if (m) m.classList.toggle('flip', at.bearing < 0); }
      done.setLatLngs(MapKit.sliceAlong(coords, p));
      if (caption) {
        if (status === 'Delivered') {
          var pct = Math.round(p * 100);
          caption.textContent = pct >= 95 ? 'Truk hampir tiba (perkiraan, bukan GPS)' : 'Truk dalam perjalanan, sekitar ' + pct + '% rute (perkiraan, bukan GPS)';
        } else if (status === 'Completed') { caption.textContent = 'Barang sudah tiba di warung'; }
        else if (status === 'Rejected' || status === 'Cancelled') { caption.textContent = 'Pesanan dihentikan'; }
        else { caption.textContent = status === 'Ready' ? 'Truk siap berangkat dari toko grosir' : 'Truk menunggu di toko grosir sampai barang siap'; }
      }
    }
    render();
    if (status === 'Delivered') setInterval(render, 5000);
  });
  setTimeout(function () { map.invalidateSize(); }, 250);
})();
