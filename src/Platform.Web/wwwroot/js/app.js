/* SIRPAS · perilaku umum antarmuka (tanpa framework, tanpa dependensi). */
(function () {
  'use strict';
  const $ = (sel, root) => (root || document).querySelector(sel);
  const $$ = (sel, root) => Array.from((root || document).querySelectorAll(sel));

  // ---------- Sidebar (ponsel)
  const side = $('#sidebar'), scrim = $('#scrim');
  function drawer(open) {
    if (!side) return;
    if (open) $$('[data-dropdown].open').forEach(function (d) { d.classList.remove('open'); });
    side.classList.toggle('open', open);
    if (scrim) scrim.classList.toggle('show', open);
    document.body.classList.toggle('drawer-open', open);
  }
  document.addEventListener('click', function (e) {
    if (e.target.closest('[data-toggle-drawer]')) { drawer(!(side && side.classList.contains('open'))); return; }
    if (e.target.closest('[data-close-drawer]')) { drawer(false); }
  });

  // Seluruh baris daftar pesanan membuka detail. Enter/Space juga mengaktifkan baris yang fokus.
  document.addEventListener('click', function (e) {
    const row = e.target.closest('tr[data-row-link]');
    if (!row || e.target.closest('a, button, input, select, textarea, label')) return;
    if (e.ctrlKey || e.metaKey) window.open(row.dataset.rowLink, '_blank', 'noopener');
    else window.location.assign(row.dataset.rowLink);
  });
  document.addEventListener('keydown', function (e) {
    const row = e.target.closest('tr[data-row-link]');
    if (!row || e.target !== row || (e.key !== 'Enter' && e.key !== ' ')) return;
    e.preventDefault();
    window.location.assign(row.dataset.rowLink);
  });

  // ---------- Sidebar: ingat posisi gulir daftar menu, biarkan kepala sidebar tetap terlihat.
  (function () {
    const nav = side && $('nav', side);
    if (!nav) return;
    const key = 'platform.nav.v1.' + (side.getAttribute('data-role') || '');
    const save = function () { try { sessionStorage.setItem(key, String(nav.scrollTop)); } catch (_) { /* penyimpanan tidak tersedia: abaikan */ } };
    nav.addEventListener('click', function (e) { if (e.target.closest('a')) save(); });
    window.addEventListener('pagehide', save);
    try { const saved = sessionStorage.getItem(key); if (saved !== null) nav.scrollTop = parseInt(saved, 10) || 0; } catch (_) { /* abaikan */ }
    // Menu aktif harus terlihat (mis. halaman dibuka lewat tautan dari tempat lain).
    const active = nav.querySelector('.nav-item.active');
    if (active) {
      const r = active.getBoundingClientRect(), s = nav.getBoundingClientRect();
      if (r.top < s.top) nav.scrollTop += r.top - s.top - 12;
      else if (r.bottom > s.bottom) nav.scrollTop += r.bottom - s.bottom + 12;
    }
  })();

  // ---------- Pratinjau gambar produk sebelum diunggah (input dengan data-img-input)
  document.addEventListener('change', function (e) {
    const input = e.target.closest('[data-img-input]');
    if (!input) return;
    const file = input.files && input.files[0];
    const box = input.closest('.img-field') && input.closest('.img-field').querySelector('.pthumb');
    if (!file || !box) return;
    if (file.size > 2 * 1024 * 1024) {
      if (window.toast) window.toast('Ukuran gambar maksimal 2 MB.', 'error');
      input.value = '';
      return;
    }
    box.className = 'pthumb';
    box.innerHTML = '';
    const img = document.createElement('img');
    img.alt = '';
    img.src = URL.createObjectURL(file);
    box.appendChild(img);
  });

  // ---------- Dropdown
  document.addEventListener('click', function (e) {
    const toggle = e.target.closest('[data-dropdown-toggle]');
    const openDropdowns = $$('[data-dropdown].open');

    if (toggle) {
      const parentDropdown = toggle.closest('[data-dropdown]');
      openDropdowns.forEach(function (d) {
        if (d !== parentDropdown) d.classList.remove('open');
      });
      if (parentDropdown) parentDropdown.classList.toggle('open');
      return;
    }

    // Klik di luar dropdown: tutup semua dropdown yang sedang terbuka
    openDropdowns.forEach(function (d) {
      if (!d.contains(e.target)) {
        d.classList.remove('open');
      }
    });
  });
  document.addEventListener('keydown', function (e) {
    if (e.key === 'Escape') { $$('[data-dropdown].open').forEach(function (d) { d.classList.remove('open'); }); drawer(false); }
  });

  // ---------- Dialog bawaan browser
  document.addEventListener('click', function (e) {
    const open = e.target.closest('[data-open-dialog]');
    if (open) { const d = $(open.getAttribute('data-open-dialog')); if (d && d.showModal) d.showModal(); return; }
    if (e.target.closest('[data-close-dialog]')) { const d = e.target.closest('dialog'); if (d) d.close(); return; }
    if (e.target.tagName === 'DIALOG' && e.target.classList.contains('modal')) { e.target.close(); }
  });

  // ---------- Konfirmasi (form/tombol dengan data-confirm)
  const confirmDlg = $('#confirm-dialog');
  function ask(message, title, okText, danger) {
    return new Promise(function (resolve) {
      if (!confirmDlg || !confirmDlg.showModal) { resolve(window.confirm(message)); return; }
      $('#confirm-title').textContent = title || 'Yakin?';
      $('#confirm-text').textContent = message;
      const yes = $('#confirm-yes');
      yes.textContent = okText || 'Ya, lanjutkan';
      yes.classList.toggle('btn-danger', !!danger);
      const done = function (v) { confirmDlg.close(); cleanup(); resolve(v); };
      const onYes = function () { done(true); };
      const onNo = function () { done(false); };
      const cleanup = function () { yes.removeEventListener('click', onYes); no.removeEventListener('click', onNo); confirmDlg.removeEventListener('cancel', onNo); };
      const no = $('[data-confirm-no]', confirmDlg);
      yes.addEventListener('click', onYes); no.addEventListener('click', onNo); confirmDlg.addEventListener('cancel', onNo);
      confirmDlg.showModal();
    });
  }
  window.askConfirm = ask;
  document.addEventListener('submit', function (e) {
    const form = e.target;
    const trigger = e.submitter && e.submitter.hasAttribute('data-confirm') ? e.submitter : (form.hasAttribute('data-confirm') ? form : null);
    if (!trigger || form.dataset.confirmed === '1') return;
    e.preventDefault();
    ask(trigger.getAttribute('data-confirm'), trigger.getAttribute('data-confirm-title'), trigger.getAttribute('data-confirm-ok'), trigger.hasAttribute('data-confirm-danger'))
      .then(function (ok) { if (ok) { form.dataset.confirmed = '1'; if (e.submitter && form.requestSubmit) form.requestSubmit(e.submitter); else form.submit(); } });
  });

  // ---------- Cegah kirim ganda & filter otomatis
  document.addEventListener('submit', function (e) {
    const form = e.target;
    if (e.defaultPrevented || !form.hasAttribute('data-once')) return;
    $$('button[type=submit]', form).forEach(function (b) { b.classList.add('disabled'); b.setAttribute('aria-busy', 'true'); });
  });
  document.addEventListener('change', function (e) {
    const t = e.target.closest('[data-autosubmit]');
    if (t) { const f = t.closest('form'); if (f) f.requestSubmit ? f.requestSubmit() : f.submit(); }
  });

  // ---------- Toast
  window.toast = function (message, type) {
    const box = $('#toasts'); if (!box) return;
    const el = document.createElement('div');
    el.className = 'toast' + (type === 'error' ? ' error' : '');
    el.textContent = message;
    box.appendChild(el);
    setTimeout(function () { el.style.transition = 'opacity .3s'; el.style.opacity = '0'; setTimeout(function () { el.remove(); }, 320); }, 4200);
  };

  // ---------- Salin teks
  document.addEventListener('click', function (e) {
    const b = e.target.closest('[data-copy]'); if (!b) return;
    const text = b.getAttribute('data-copy') || ($(b.getAttribute('data-copy-from')) || {}).textContent || '';
    (navigator.clipboard ? navigator.clipboard.writeText(text) : Promise.reject()).then(
      function () { window.toast('Tersalin ke papan klip.'); },
      function () { window.toast('Tidak dapat menyalin. Salin manual ya.', 'error'); });
  });

  // ---------- Notifikasi: periksa berkala (murah: satu GET ringan tiap 45 detik, berhenti saat tab tidak aktif)
  const bellCount = $('#bell-count');
  async function pollNotifications() {
    if (!bellCount || document.hidden) return;
    try {
      const res = await fetch('/Notifications/Summary', { headers: { Accept: 'application/json' }, credentials: 'same-origin' });
      if (!res.ok) return;
      const data = await res.json();
      bellCount.textContent = data.total > 99 ? '99+' : String(data.total);
      bellCount.classList.toggle('hide', data.total === 0);
      const list = $('#bell-list');
      if (list) {
        list.innerHTML = data.items.length === 0
          ? '<div class="empty" style="padding:22px 12px"><p class="mb-0">Semua beres. Belum ada yang perlu Anda lakukan.</p></div>'
          : data.items.map(function (n) {
              return '<a class="note ' + n.tone + '" href="' + n.url + '"><span class="ico">' + n.iconSvg + '</span><span><b></b><span></span></span></a>';
            }).join('');
        $$('a.note', list).forEach(function (a, i) { a.querySelector('b').textContent = data.items[i].title; a.querySelector('span span:last-child').textContent = data.items[i].detail; });
      }
      $$('.nav-item[data-badge-key]').forEach(function (a) {
        const key = a.getAttribute('data-badge-key'); if (!key) return;
        const n = key === 'expiring'
          ? (data.badges.expiring || 0) + (data.badges.expired || 0)
          : (data.badges[key] || 0);
        let b = $('.nav-badge', a);
        if (n > 0) { if (!b) { b = document.createElement('span'); b.className = 'nav-badge'; a.appendChild(b); } b.textContent = n > 99 ? '99+' : String(n); }
        else if (b) { b.remove(); }
      });
    } catch (_) { /* jaringan putus: abaikan, coba lagi nanti */ }
  }
  if (bellCount) {
    setInterval(pollNotifications, 45000);
    document.addEventListener('visibilitychange', function () { if (!document.hidden) pollNotifications(); });
  }

  // ---------- Galeri foto halaman depan
  (function () {
    const gallery = $('[data-lp-gallery]');
    if (!gallery) return;
    const slides = $$('[data-lp-slide]', gallery);
    const dots = $$('[data-lp-dot]', gallery);
    const counter = $('[data-lp-counter]', gallery);
    let activeIndex = 0;
    let timer = null;

    function stopAutoplay() {
      if (timer !== null) window.clearTimeout(timer);
      timer = null;
    }

    function scheduleAutoplay() {
      stopAutoplay();
      if (document.hidden || gallery.contains(document.activeElement)) return;
      timer = window.setTimeout(function () {
        showSlide(activeIndex + 1);
        scheduleAutoplay();
      }, 5200);
    }

    function showSlide(index) {
      activeIndex = (index + slides.length) % slides.length;
      slides.forEach(function (slide, i) {
        const isActive = i === activeIndex;
        slide.classList.toggle('is-active', isActive);
        slide.setAttribute('aria-hidden', String(!isActive));
      });
      dots.forEach(function (dot, i) {
        const isActive = i === activeIndex;
        dot.classList.toggle('is-active', isActive);
        dot.setAttribute('aria-pressed', String(isActive));
      });
      if (counter) counter.textContent = String(activeIndex + 1).padStart(2, '0') + ' / ' + String(slides.length).padStart(2, '0');
    }

    $('[data-lp-prev]', gallery).addEventListener('click', function () { showSlide(activeIndex - 1); scheduleAutoplay(); });
    $('[data-lp-next]', gallery).addEventListener('click', function () { showSlide(activeIndex + 1); scheduleAutoplay(); });
    dots.forEach(function (dot) {
      dot.addEventListener('click', function () {
        showSlide(Number(dot.getAttribute('data-lp-dot')));
        scheduleAutoplay();
      });
    });
    gallery.addEventListener('focusin', stopAutoplay);
    gallery.addEventListener('focusout', function (event) {
      if (!event.relatedTarget || !gallery.contains(event.relatedTarget)) window.setTimeout(scheduleAutoplay, 0);
    });
    gallery.addEventListener('keydown', function (event) {
      if (event.key === 'ArrowLeft') { event.preventDefault(); showSlide(activeIndex - 1); scheduleAutoplay(); }
      if (event.key === 'ArrowRight') { event.preventDefault(); showSlide(activeIndex + 1); scheduleAutoplay(); }
    });
    document.addEventListener('visibilitychange', scheduleAutoplay);
    scheduleAutoplay();
  })();

  // ---------- Normalisasi koma (,) ke titik (.) pada keyboard desimal Android / Indonesia
  document.addEventListener('input', function (e) {
    var target = e.target;
    if (target && target.tagName === 'INPUT' && target.getAttribute('inputmode') === 'decimal') {
      if (typeof target.value === 'string' && target.value.includes(',')) {
        var start = target.selectionStart;
        var end = target.selectionEnd;
        target.value = target.value.replace(/,/g, '.');
        if (start !== null && end !== null) {
          try { target.setSelectionRange(start, end); } catch (_) {}
        }
      }
    }
  });

  // ---------- Util bersama untuk skrip halaman
  window.fmtRp = function (n) { return 'Rp' + Math.round(n).toLocaleString('id-ID'); };
  window.fmtNum = function (n) { return Number(n).toLocaleString('id-ID'); };
  window.escapeHtml = function (s) { return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) { return ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]; }); };
})();
