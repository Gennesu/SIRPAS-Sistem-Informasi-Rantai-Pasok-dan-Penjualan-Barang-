/* Peta interaktif gaya aplikasi belanja: daftar di samping dan peta saling terhubung.
   Peta grosir dipakai warung dan peternakan (mode "retail"), peta persebaran warung dipakai grosir (mode "grosir").
   Semua data dibaca dari kartu yang dirender server, jadi daftar tetap berfungsi bila ubin peta gagal dimuat. */
(function () {
  'use strict';
  var shell = document.querySelector('[data-map-explorer]');
  if (!shell || !window.L || !window.MapKit) return;
  var mode = shell.dataset.mode;
  var oLat = parseFloat(shell.dataset.originLat), oLon = parseFloat(shell.dataset.originLon);
  var origin = isFinite(oLat) && isFinite(oLon) ? [oLat, oLon] : null;
  var originLabel = shell.dataset.originLabel || 'Toko Anda';
  var originName = shell.dataset.originName || (mode === 'grosir' ? originLabel : 'Lokasi Anda');

  var map = MapKit.createMap(shell.querySelector('.map-el'), { center: origin || MapKit.DEFAULT_CENTER, zoom: 13 });
  var list = document.getElementById('list');
  var noMatch = document.getElementById('no-match');
  var input = document.getElementById('q');
  var filter = 'all', query = '', active = null, circle = null, nearCircle = null;

  // Titik asal: toko grosir (pin) atau lokasi warung (titik biru berdenyut).
  if (origin) {
    if (mode === 'grosir') {
      MapKit.pin(origin, 'origin', { title: originName, z: 500 }).addTo(map).bindTooltip('<b>' + window.escapeHtml(originName) + '</b><br>' + window.escapeHtml(originLabel), { direction: 'top', offset: [0, -50] });
      var radius = parseFloat(shell.dataset.radius);
      if (isFinite(radius) && radius > 0) {
        circle = L.circle(origin, { radius: radius * 1000, color: '#a8531b', weight: 2, dashArray: '6 8', fillColor: '#a8531b', fillOpacity: 0.07, interactive: false }).addTo(map);
      }
    } else {
      MapKit.youMarker(origin).addTo(map).bindTooltip(window.escapeHtml(originName), { direction: 'top' });
    }
  }

  var entries = Array.prototype.slice.call(shell.querySelectorAll('[data-card]')).map(function (card) {
    var d = card.dataset;
    var latlng = [parseFloat(d.lat), parseFloat(d.lon)];
    var marker = MapKit.pin(latlng, d.kind, { label: d.label || undefined, title: d.name });
    var tpl = card.querySelector('template.pop');
    if (tpl) marker.bindPopup(tpl.innerHTML, { maxWidth: 300, autoPanPaddingTopLeft: [30, 90], autoPanPaddingBottomRight: [30, 30] });
    var entry = { card: card, marker: marker, latlng: latlng, shown: false };
    marker.on('click', function () { select(entry, false); });
    card.addEventListener('click', function (e) { if (e.target.closest('a,button')) return; select(entry, true); });
    card.addEventListener('keydown', function (e) { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); select(entry, true); } });
    card.addEventListener('mouseenter', function () { if (entry.shown) MapKit.setPinBig(marker, true); });
    card.addEventListener('mouseleave', function () { if (active !== entry) MapKit.setPinBig(marker, false); });
    return entry;
  });

  function matches(e) {
    var d = e.card.dataset;
    if (query && (d.search || '').indexOf(query) === -1) return false;
    if (filter.indexOf('max:') === 0) { var maxKm = parseFloat(filter.slice(4)), dist = parseFloat(d.distance); return isFinite(dist) && dist <= maxKm; }
    switch (filter) {
      case 'deals': return parseInt(d.deals, 10) > 0;
      case 'delivery': return d.delivery === '1';
      case 'near': return isFinite(parseFloat(d.distance)) && parseFloat(d.distance) <= 5;
      case 'customer': return d.customer === '1';
      case 'prospect': return d.customer === '0';
      case 'range': return d.inrange === '1';
      default: return true;
    }
  }

  function apply(refit) {
    var visible = 0;
    entries.forEach(function (e) {
      var ok = matches(e);
      e.card.classList.toggle('hide', !ok);
      if (ok && !e.shown) { e.marker.addTo(map); e.shown = true; }
      if (!ok && e.shown) { map.removeLayer(e.marker); e.shown = false; if (active === e) active = null; }
      if (ok) visible++;
    });
    if (noMatch) noMatch.classList.toggle('hide', visible > 0 || entries.length === 0);
    if (mode === 'retail' && origin) {
      if (filter === 'near' && !nearCircle) nearCircle = L.circle(origin, { radius: 5000, color: '#2563c9', weight: 2, dashArray: '6 8', fillColor: '#2563c9', fillOpacity: 0.06, interactive: false }).addTo(map);
      if (filter !== 'near' && nearCircle) { map.removeLayer(nearCircle); nearCircle = null; }
    }
    if (refit) fit();
  }

  function fit() {
    var pts = entries.filter(function (e) { return e.shown; }).map(function (e) { return e.latlng; });
    if (origin) pts.push(origin);
    if (pts.length === 0) return;
    if (pts.length === 1) { map.setView(pts[0], 15); return; }
    map.fitBounds(L.latLngBounds(pts), { padding: [60, 60], maxZoom: 15 });
  }

  function select(entry, fromList) {
    if (active && active !== entry) { active.card.classList.remove('active'); MapKit.setPinBig(active.marker, false); }
    active = entry;
    entry.card.classList.add('active');
    drawRoute(entry);
    MapKit.setPinBig(entry.marker, true);
    if (!fromList) { try { entry.card.scrollIntoView({ block: 'nearest', behavior: 'smooth' }); } catch (_) {} }
    if (fromList) {
      map.flyTo(entry.latlng, Math.max(map.getZoom(), 15), { duration: 0.6 });
      setTimeout(function () { entry.marker.openPopup(); }, 650);
      var canvas = shell.querySelector('.map-canvas');
      if (window.matchMedia('(max-width: 960px)').matches && canvas) { canvas.scrollIntoView({ behavior: 'smooth', block: 'start' }); }
    }
  }
  // Rute jalan tersingkat dari titik asal ke lokasi terpilih (OSRM publik). Tanpa internet: garis lurus putus-putus.
  var routeLine = null, routeInfo = document.getElementById('route-info');
  function km(m) { return (m / 1000).toFixed(1).replace('.', ','); }
  function showRoute(geo, text) {
    if (routeLine) map.removeLayer(routeLine);
    routeLine = geo.addTo(map);
    if (routeInfo) routeInfo.textContent = text;
  }
  function drawRoute(entry) {
    if (shell.dataset.route !== '1' || !origin) return;
    var a = origin, b = entry.latlng;
    var straight = function () { showRoute(L.polyline([a, b], { color: '#2347d1', weight: 4, dashArray: '6 8' }), 'Rute jalan belum tersedia; garis lurus hanya sebagai gambaran.'); };
    fetch('https://router.project-osrm.org/route/v1/driving/' + a[1] + ',' + a[0] + ';' + b[1] + ',' + b[0] + '?overview=full&geometries=geojson')
      .then(function (r) { return r.json(); }).then(function (j) {
        var r0 = j.routes && j.routes[0];
        if (!r0) return straight();
        showRoute(L.geoJSON(r0.geometry, { style: { color: '#2347d1', weight: 5 } }), 'Rute jalan tersingkat: ' + km(r0.distance) + ' km, sekitar ' + Math.round(r0.duration / 60) + ' menit. Arah sebaliknya memakai jalur yang sama.');
      }).catch(straight);
  }

  map.on('popupclose', function () { if (active) { active.card.classList.remove('active'); MapKit.setPinBig(active.marker, false); active = null; } });

  // Filter & pencarian
  var chips = document.getElementById('filters');
  if (chips) chips.addEventListener('click', function (e) {
    var c = e.target.closest('[data-filter]'); if (!c) return;
    Array.prototype.forEach.call(chips.querySelectorAll('.chip'), function (x) { x.classList.toggle('active', x === c); });
    filter = c.dataset.filter; apply(true);
  });
  if (input) input.addEventListener('input', function () { query = input.value.trim().toLowerCase(); apply(true); });

  // Tombol HUD
  var fitBtn = document.getElementById('btn-fit'); if (fitBtn) fitBtn.addEventListener('click', fit);
  var radiusBtn = document.getElementById('btn-radius');
  if (radiusBtn && circle) radiusBtn.addEventListener('click', function () { var on = radiusBtn.classList.toggle('on'); if (on) circle.addTo(map); else map.removeLayer(circle); });
  var meBtn = document.getElementById('btn-me'), gpsMarker = null;
  if (meBtn) meBtn.addEventListener('click', function () {
    if (!navigator.geolocation) { if (window.toast) window.toast('Peramban tidak mendukung lokasi.', 'error'); if (origin) map.flyTo(origin, 15); return; }
    meBtn.classList.add('on');
    navigator.geolocation.getCurrentPosition(function (pos) {
      meBtn.classList.remove('on');
      var p = [pos.coords.latitude, pos.coords.longitude];
      if (gpsMarker) map.removeLayer(gpsMarker);
      gpsMarker = MapKit.youMarker(p).addTo(map);
      map.flyTo(p, 15, { duration: 0.7 });
    }, function () {
      meBtn.classList.remove('on');
      if (window.toast) window.toast('Lokasi tidak dapat dibaca. Menampilkan lokasi usaha Anda.', 'error');
      if (origin) map.flyTo(origin, 15);
    }, { enableHighAccuracy: true, timeout: 10000 });
  });

  apply(true);
  setTimeout(function () { map.invalidateSize(); fit(); }, 300);
})();
