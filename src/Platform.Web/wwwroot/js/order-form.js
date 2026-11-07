/* Form pesanan ke grosir: subtotal, ongkir, total, dan progres minimal pesanan dihitung langsung di browser.
   Perhitungan resmi (termasuk ongkir menurut jarak) tetap dilakukan server. */
(function () {
  'use strict';
  var form = document.getElementById('order-form');
  if (!form) return;
  var fee = parseFloat(form.dataset.fee) || 0, min = parseFloat(form.dataset.min) || 0;
  var qtys = Array.prototype.slice.call(form.querySelectorAll('.order-qty'));
  var submit = document.getElementById('order-submit');

  function calc() {
    var subtotal = 0, any = false;
    qtys.forEach(function (i) { var q = parseInt(i.value, 10) || 0; if (q > 0) any = true; subtotal += q * (parseFloat(i.dataset.price) || 0); });
    var del = form.querySelector('input[name=Fulfillment][value=Delivery]');
    var useFee = del && del.checked && !del.disabled ? fee : 0;
    var addr = document.getElementById('address-field');
    if (addr) addr.classList.toggle('hide', !(del && del.checked && !del.disabled));
    document.getElementById('order-subtotal').textContent = fmtRp(subtotal);
    document.getElementById('order-fee').textContent = any && useFee === 0 && del && del.checked ? 'Gratis' : fmtRp(any ? useFee : 0);
    document.getElementById('order-total').textContent = fmtRp(any ? subtotal + useFee : 0);
    var hint = document.getElementById('order-hint'), bar = document.getElementById('order-min-bar');
    var ok = any && subtotal >= min;
    if (min > 0 && hint && bar) {
      bar.style.width = Math.min(100, min > 0 ? subtotal * 100 / min : 100) + '%';
      hint.textContent = !any ? 'Minimal pesanan ' + fmtRp(min) + '.' : ok ? 'Minimal pesanan terpenuhi.' : 'Kurang ' + fmtRp(min - subtotal) + ' lagi untuk memenuhi minimal pesanan ' + fmtRp(min) + '.';
    }
    if (submit) submit.classList.toggle('disabled', !ok);
  }

  form.addEventListener('click', function (e) {
    var s = e.target.closest('[data-step]'); if (!s) return;
    var i = s.parentElement.querySelector('input');
    i.value = Math.max(0, (parseInt(i.value, 10) || 0) + parseInt(s.dataset.step, 10)) || '';
    calc();
  });
  form.addEventListener('input', calc);
  form.addEventListener('change', calc);
  form.addEventListener('submit', function (e) { if (submit && submit.classList.contains('disabled')) e.preventDefault(); });
  calc();
})();
