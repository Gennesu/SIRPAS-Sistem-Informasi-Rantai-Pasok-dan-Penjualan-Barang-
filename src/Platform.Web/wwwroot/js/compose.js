/* Composer paket anti-mubazir: menghitung harga, diskon, hemat warung, dan untung secara langsung. */
(function () {
  'use strict';
  var form = document.getElementById('compose-form');
  if (!form) return;
  var items = Array.prototype.slice.call(form.querySelectorAll('[data-item]'));
  var slider = document.getElementById('discount');
  var label = document.getElementById('discount-label');
  var fixed = document.getElementById('fixed-price');
  var hint = document.getElementById('discount-hint');
  var $ = function (id) { return document.getElementById(id); };
  var touched = false;

  function qtyOf(row) { return Math.max(0, parseInt(row.querySelector('input[type=number]').value, 10) || 0); }

  function suggestFor(days) { return days <= 3 ? 50 : days <= 7 ? 40 : days <= 14 ? 30 : days <= 30 ? 20 : 10; }

  function recalc() {
    var normal = 0, cost = 0, units = 0, minDays = null, avail = null, any = false;
    items.forEach(function (row) {
      var q = qtyOf(row);
      row.classList.toggle('on', q > 0);
      if (q <= 0) return;
      any = true;
      var n = parseFloat(row.dataset.normal), c = parseFloat(row.dataset.cost), d = parseInt(row.dataset.days, 10), r = parseInt(row.dataset.remaining, 10);
      normal += n * q; cost += c * q; units += q;
      minDays = minDays === null ? d : Math.min(minDays, d);
      var a = Math.floor(r / q);
      avail = avail === null ? a : Math.min(avail, a);
    });
    normal = Math.round(normal * 100) / 100;

    var percent = parseInt(slider.value, 10) || 0;
    var price;
    var fixedVal = parseFloat(fixed.value);
    if (isFinite(fixedVal) && fixedVal > 0) {
      price = Math.min(normal, fixedVal);
      percent = normal > 0 ? Math.round((1 - price / normal) * 100) : 0;
    } else {
      price = Math.min(normal, Math.max(100, Math.round(normal * (100 - percent) / 100 / 100) * 100));
      if (!any) price = 0;
      percent = normal > 0 && any ? Math.round((1 - price / normal) * 100) : percent;
    }

    label.textContent = (parseInt(slider.value, 10) || 0) + '%';
    $('sum-price').textContent = fmtRp(price);
    $('sum-normal').textContent = fmtRp(normal);
    $('sum-off').textContent = '-' + percent + '%';
    $('sum-save').textContent = fmtRp(normal - price);
    $('sum-units').textContent = fmtNum(units) + ' item';
    $('sum-avail').textContent = avail === null ? '0' : fmtNum(avail);
    $('sum-cost').textContent = fmtRp(cost);
    var margin = price - cost;
    var m = $('sum-margin'); m.textContent = (margin < 0 ? '-' : '') + fmtRp(Math.abs(margin)); m.style.color = margin < 0 ? 'var(--red-700)' : 'var(--brand-700)';
    $('sum-loss').classList.toggle('hide', !(any && margin < 0));
    $('sum-empty').classList.toggle('hide', any);
    $('submit-btn').classList.toggle('disabled', !any);

    var tag = $('sum-tag'), days = minDays === null ? null : minDays;
    $('sum-days').textContent = days === null ? '–' : Math.max(days, 0);
    tag.className = 'daytag sm band-' + (days === null ? 'safe' : days < 0 ? 'expired' : days <= 7 ? 'critical' : days <= 14 ? 'soon' : days <= 30 ? 'watch' : 'safe');
    if (days !== null && !touched && !(isFinite(fixedVal) && fixedVal > 0)) { slider.value = suggestFor(days); label.textContent = slider.value + '%'; }
    if (hint && days !== null) hint.textContent = 'Saran untuk barang yang ' + Math.max(days, 0) + ' hari lagi kadaluarsa: ' + suggestFor(days) + '%.';

    var chosen = form.querySelectorAll('[data-target]:checked').length;
    var lbl = $('submit-label');
    lbl.textContent = chosen > 0 ? 'Buat paket & kirim ke ' + chosen + ' warung' : 'Buat paket & tayangkan di marketplace';
    Array.prototype.forEach.call(form.querySelectorAll('[data-discount]'), function (c) { c.setAttribute('aria-pressed', String(parseInt(c.dataset.discount, 10) === parseInt(slider.value, 10) && !(isFinite(fixedVal) && fixedVal > 0))); });
  }

  form.addEventListener('click', function (e) {
    var step = e.target.closest('[data-step]');
    if (step) {
      var input = step.parentElement.querySelector('input[type=number]');
      var next = (parseInt(input.value, 10) || 0) + parseInt(step.dataset.step, 10);
      var min = input.min === '' ? 0 : parseInt(input.min, 10), max = input.max === '' ? Infinity : parseInt(input.max, 10);
      input.value = Math.max(min, Math.min(max, next));
      recalc();
      return;
    }
    var chip = e.target.closest('[data-discount]');
    if (chip) { touched = true; fixed.value = ''; slider.value = chip.dataset.discount; recalc(); }
  });
  form.addEventListener('input', function (e) {
    if (e.target === slider) { touched = true; fixed.value = ''; }
    if (e.target === fixed && fixed.value) { touched = true; }
    recalc();
  });
  form.addEventListener('change', recalc);
  form.addEventListener('submit', function (e) { if (form.querySelector('#submit-btn').classList.contains('disabled')) { e.preventDefault(); } });
  recalc();
})();
