/* Pemilih lokasi gaya aplikasi belanja: pin tetap di tengah, geser peta untuk menentukan titik.
   Pencarian alamat dan nama jalan memakai Nominatim (OpenStreetMap, gratis; batas wajar 1 permintaan/detik, cukup untuk demo/kampus). */
(function () {
  'use strict';
  var root = document.querySelector('[data-picker]');
  if (!root || !window.L || !window.MapKit) return;

  var latEl = document.getElementById(root.dataset.lat);
  var lonEl = document.getElementById(root.dataset.lon);
  var addrEl = root.dataset.address ? document.getElementById(root.dataset.address) : null;
  var canvas = root.querySelector('.picker-map');
  var label = root.querySelector('[data-addr-label]');
  var search = root.querySelector('[data-search]');
  var results = root.querySelector('.results');
  var useBtn = root.querySelector('[data-use-point]');

  function num(el) { var v = parseFloat(String(el.value).replace(',', '.')); return isFinite(v) ? v : null; }
  var lat = num(latEl), lon = num(lonEl);
  var has = lat !== null && lon !== null;

  var map = MapKit.createMap(canvas, { center: has ? [lat, lon] : MapKit.DEFAULT_CENTER, zoom: has ? 16 : 13 });
  var userMoved = false, geoTimer = null, geoCtl = null, lastText = '';

  function setInputs(c) {
    latEl.value = c.lat.toFixed(6);
    lonEl.value = c.lng.toFixed(6);
    latEl.dispatchEvent(new Event('input', { bubbles: true }));
    if (useBtn) useBtn.classList.add('hide');
  }

  var ONLINE = !!(window.PLATFORM_MAP && window.PLATFORM_MAP.online);

  // V3: tidak ada geocoding eksternal. Koordinat berasal dari geseran peta/GPS peramban; alamat diisi manual.
  function reverse(c) { /* sengaja kosong */ }

  map.on('dragstart zoomstart', function () { userMoved = true; });
  map.on('moveend', function () {
    if (!userMoved && !has) return;
    var c = map.getCenter();
    setInputs(c);
    reverse(c);
  });

  if (!has && useBtn) {
    useBtn.classList.remove('hide');
    useBtn.addEventListener('click', function () { var c = map.getCenter(); setInputs(c); reverse(c); });
  }

  // Ketik koordinat manual -> peta ikut pindah.
  [latEl, lonEl].forEach(function (el) {
    el.addEventListener('change', function () {
      var a = num(latEl), b = num(lonEl);
      if (a !== null && b !== null) { userMoved = false; map.setView([a, b], 16); }
    });
  });

  // Pencarian alamat
  var timer = null;
  function showResults(items) {
    if (!results) return;
    if (!items.length) { results.style.display = 'none'; results.innerHTML = ''; return; }
    results.innerHTML = items.map(function (it, i) {
      return '<button type="button" data-i="' + i + '"><span>' + (window.escapeHtml ? window.escapeHtml(it.display_name) : it.display_name) + '</span></button>';
    }).join('');
    results.style.display = 'block';
    Array.prototype.forEach.call(results.querySelectorAll('button'), function (b) {
      b.addEventListener('click', function () {
        var it = items[parseInt(b.getAttribute('data-i'), 10)];
        userMoved = true;
        map.setView([parseFloat(it.lat), parseFloat(it.lon)], 17);
        results.style.display = 'none';
        search.value = '';
      });
    });
  }
  function doSearch() {
    var q = (search.value || '').trim();
    if (q.length < 3) { showResults([]); return; }
    if (window.toast) window.toast('Pencarian alamat memerlukan layanan online dan dinonaktifkan di mode lokal. Isi alamat manual atau geser peta ke lokasi.', 'info');
  }
  if (search) {
    search.addEventListener('input', function () { clearTimeout(timer); timer = setTimeout(doSearch, 650); });
    search.addEventListener('keydown', function (e) { if (e.key === 'Enter') { e.preventDefault(); clearTimeout(timer); doSearch(); } });
  }

  // Lokasi saya (GPS peramban, gratis)
  var locate = root.querySelector('[data-locate]');
  if (locate) {
    locate.addEventListener('click', function () {
      if (!navigator.geolocation) { if (window.toast) window.toast('Peramban tidak mendukung lokasi.', 'error'); return; }
      locate.classList.add('on');
      navigator.geolocation.getCurrentPosition(function (pos) {
        locate.classList.remove('on');
        userMoved = true;
        map.setView([pos.coords.latitude, pos.coords.longitude], 17);
      }, function () {
        locate.classList.remove('on');
        if (window.toast) window.toast('Lokasi tidak dapat dibaca. Izinkan akses lokasi atau cari alamat.', 'error');
      }, { enableHighAccuracy: true, timeout: 10000 });
    });
  }

  setTimeout(function () { map.invalidateSize(); }, 200);
})();
