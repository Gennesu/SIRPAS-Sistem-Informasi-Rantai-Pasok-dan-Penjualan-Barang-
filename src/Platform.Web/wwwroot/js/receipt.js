/* Struk: kode batang Code 39 (hiasan/identitas nomor struk), pilihan ukuran kertas 58/80 mm, dan cetak. */
(function () {
  'use strict';
  var T = { '0': 'nnnwwnwnn', '1': 'wnnwnnnnw', '2': 'nnwwnnnnw', '3': 'wnwwnnnnn', '4': 'nnnwwnnnw', '5': 'wnnwwnnnn', '6': 'nnwwwnnnn', '7': 'nnnwnnwnw', '8': 'wnnwnnwnn', '9': 'nnwwnnwnn',
    A: 'wnnnnwnnw', B: 'nnwnnwnnw', C: 'wnwnnwnnn', D: 'nnnnwwnnw', E: 'wnnnwwnnn', F: 'nnwnwwnnn', G: 'nnnnnwwnw', H: 'wnnnnwwnn', I: 'nnwnnwwnn', J: 'nnnnwwwnn',
    K: 'wnnnnnnww', L: 'nnwnnnnww', M: 'wnwnnnnwn', N: 'nnnnwnnww', O: 'wnnnwnnwn', P: 'nnwnwnnwn', Q: 'nnnnnnwww', R: 'wnnnnnwwn', S: 'nnwnnnwwn', T: 'nnnnwnwwn',
    U: 'wwnnnnnnw', V: 'nwwnnnnnw', W: 'wwwnnnnnn', X: 'nwnnwnnnw', Y: 'wwnnwnnnn', Z: 'nwwnwnnnn', '-': 'nwnnnnwnw', '.': 'wwnnnnwnn', ' ': 'nwwnnnwnn', '*': 'nwnnwnwnn' };

  var svg = document.getElementById('barcode');
  if (svg) {
    var code = ('*' + String(svg.dataset.code || '').toUpperCase().replace(/[^0-9A-Z\-. ]/g, '') + '*');
    var x = 0, rects = '';
    code.split('').forEach(function (ch, idx) {
      var p = T[ch]; if (!p) return;
      for (var i = 0; i < 9; i++) {
        var w = p[i] === 'w' ? 3 : 1;
        if (i % 2 === 0) rects += '<rect x="' + x + '" y="0" width="' + w + '" height="46" fill="#111"/>';
        x += w;
      }
      if (idx < code.length - 1) x += 1;
    });
    svg.setAttribute('viewBox', '0 0 ' + x + ' 46');
    svg.setAttribute('preserveAspectRatio', 'none');
    svg.innerHTML = rects;
  }

  var receipt = document.getElementById('receipt');
  var pageStyle = document.getElementById('page-size');
  function paper(mm) {
    if (!receipt) return;
    receipt.classList.toggle('w58', mm === '58');
    if (pageStyle) pageStyle.textContent = '@page { size: ' + mm + 'mm auto; margin: ' + (mm === '58' ? '2' : '3') + 'mm; }';
    try { localStorage.setItem('struk-kertas', mm); } catch (_) {}
  }
  var saved = null; try { saved = localStorage.getItem('struk-kertas'); } catch (_) {}
  document.querySelectorAll('input[name=paper]').forEach(function (r) {
    if (saved && r.value === saved) r.checked = true;
    r.addEventListener('change', function () { paper(r.value); });
  });
  var checked = document.querySelector('input[name=paper]:checked'); if (checked) paper(checked.value);
  var print = document.getElementById('btn-print'); if (print) print.addEventListener('click', function () { window.print(); });
  if (/[?&]baru=1/.test(location.search) && print) { setTimeout(function () { print.focus(); }, 300); }
})();
