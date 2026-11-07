// Form pembelian: isi dropdown satuan sesuai produk dan hitung subtotal/total di browser.
// Perhitungan resmi tetap dilakukan server (PurchasingService).
(function () {
  'use strict';
  var money = new Intl.NumberFormat('id-ID', { style: 'currency', currency: 'IDR', maximumFractionDigits: 0 });
  var rows = document.querySelectorAll('.pl-row');
  var totalEl = document.getElementById('pl-total');

  function rebuildUnits(row) {
    var productSel = row.querySelector('.pl-product');
    var unitSel = row.querySelector('.pl-unit');
    var wanted = unitSel.getAttribute('data-selected') || unitSel.value;
    unitSel.innerHTML = '';
    var opt = productSel.options[productSel.selectedIndex];
    var raw = opt ? opt.getAttribute('data-units') : null;
    if (!raw) { return; }
    raw.split('|').forEach(function (pair) {
      var parts = pair.split(':');
      var o = document.createElement('option');
      o.value = parts[0];
      o.textContent = parts[1] === '1' ? parts[0] : parts[0] + ' (isi ' + parts[1] + ')';
      if (parts[0] === wanted) { o.selected = true; }
      unitSel.appendChild(o);
    });
    unitSel.setAttribute('data-selected', '');
  }

  function recalc() {
    var total = 0;
    rows.forEach(function (row) {
      var qty = parseFloat(row.querySelector('.pl-qty').value) || 0;
      var price = parseFloat(row.querySelector('.pl-price').value) || 0;
      var sub = qty * price;
      row.querySelector('.pl-sub').textContent = money.format(sub);
      total += sub;
    });
    totalEl.textContent = money.format(total);
  }

  rows.forEach(function (row) {
    rebuildUnits(row);
    row.querySelector('.pl-product').addEventListener('change', function () {
      row.querySelector('.pl-unit').setAttribute('data-selected', '');
      rebuildUnits(row);
    });
    row.querySelector('.pl-qty').addEventListener('input', recalc);
    row.querySelector('.pl-price').addEventListener('input', recalc);
  });
  recalc();
})();
