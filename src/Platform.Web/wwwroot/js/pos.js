/* Kasir SIRPAS.
   Tunai + QRIS dummy + Transfer dummy.
   Semua transaksi yang dikonfirmasi tetap menggunakan endpoint
   checkout dan SalesService utama yang sudah ada.
*/
(function () {
  'use strict';

  var root = document.getElementById('pos');
  if (!root) return;

  var $ = function (id) {
    return document.getElementById(id);
  };

  var searchBox = $('pos-search');
  var resultsEl = $('pos-results');
  var cartEl = $('pos-cart');
  var totalEl = $('pos-total');

  var confirmBtn = $('pos-confirm');
  var messageEl = $('pos-message');
  var paidEl = $('pos-paid');
  var changeBox = $('change-box');
  var changeEl = $('pos-change');

  var cashBox = $('cash-box');
  var quickEl = $('quick-cash');
  var cartEl2 = $('cart');

  var mockPaymentBox = $('mock-payment');
  var qrisPanel = $('qris-panel');
  var transferPanel = $('transfer-panel');
  var mockStatus = $('mock-payment-status');

  var qrisTotalEl = $('mock-qris-total');
  var transferTotalEl = $('mock-transfer-total');

  var qrisSimulateBtn = $('qris-simulate');
  var transferSimulateBtn = $('transfer-simulate');
  var copyTransferBtn = $('copy-transfer');

  var productsUrl = root.dataset.productsUrl;
  var checkoutUrl = root.dataset.checkoutUrl;
  var receiptUrl = root.dataset.receiptUrl;

  var tokenEl = document.querySelector(
    'input[name="__RequestVerificationToken"]'
  );

  var token = tokenEl ? tokenEl.value : '';

  var cart = [];
  var products = {};
  var timer = null;
  var key = newKey();
  var busy = false;
  var mockPaidFor = null;

  function newKey() {
    return window.crypto && window.crypto.randomUUID
      ? window.crypto.randomUUID()
      : 'k' + Date.now() + Math.random().toString(16).slice(2);
  }

  function method() {
    var selected = document.querySelector(
      'input[name="pos-method"]:checked'
    );

    return selected ? selected.value : 'CASH';
  }

  function total() {
    return cart.reduce(function (sum, item) {
      return sum + item.price * item.qty;
    }, 0);
  }

  // Pembayaran simulasi terikat ke isi keranjang dan total.
  // Perubahan barang, jumlah, total, atau metode membatalkan simulasi.
  function paymentSnapshot() {
    return method() + '|' + total() + '|' +
      cart.map(function (item) {
        return item.id + ':' + item.qty;
      }).sort().join(',');
  }

  function setMockStatus(text, visible) {
    if (!mockStatus) return;

    mockStatus.textContent = text;
    mockStatus.classList.toggle('hide', !visible);
  }

  // Membuat QR visual dummy. Ini bukan QR pembayaran yang valid.
  function drawDummyQr(el) {
    if (!el || el.dataset.built) return;

    el.dataset.built = '1';

    var size = 21;

    function finder(x, y, ox, oy) {
      var dx = x - ox;
      var dy = y - oy;

      if (dx < 0 || dy < 0 || dx > 6 || dy > 6) {
        return null;
      }

      return dx === 0 ||
        dy === 0 ||
        dx === 6 ||
        dy === 6 ||
        (dx >= 2 && dx <= 4 && dy >= 2 && dy <= 4);
    }

    for (var y = 0; y < size; y++) {
      for (var x = 0; x < size; x++) {
        var dark = finder(x, y, 0, 0);

        if (dark === null) {
          dark = finder(x, y, 14, 0);
        }

        if (dark === null) {
          dark = finder(x, y, 0, 14);
        }

        if (dark === null) {
          dark = ((x * 7 + y * 11 + x * y) % 5) < 2;
        }

        var cell = document.createElement('span');
        cell.className = dark ? 'dark' : 'light';
        el.appendChild(cell);
      }
    }
  }

  drawDummyQr($('dummy-qr'));

  // ==========================================================
  // PRODUK
  // ==========================================================

  function loadProducts() {
    var q = searchBox.value.trim();

    fetch(productsUrl + '?q=' + encodeURIComponent(q), {
      credentials: 'same-origin'
    })
      .then(function (response) {
        if (!response.ok) {
          throw new Error('Gagal mengambil daftar produk.');
        }

        return response.json();
      })
      .then(function (items) {
        products = {};

        if (!items.length) {
          resultsEl.innerHTML =
            '<div class="empty" style="grid-column:1/-1">' +
              '<h3>Produk tidak ditemukan</h3>' +
              '<p>Pastikan stok lebih dari 0 dan harga jual sudah diisi di menu Stok.</p>' +
            '</div>';
          return;
        }

        resultsEl.innerHTML = items.map(function (product) {
          products[product.id] = product;

          var thumb = product.image
            ? '<span class="p-img"><img src="' + escapeHtml(product.image) + '" alt="" loading="lazy"></span>'
            : '<span class="p-img ph"><b>' + escapeHtml(String(product.name || '?').charAt(0).toUpperCase()) + '</b></span>';

          return (
            '<button type="button" class="pos-item" data-id="' +
              product.id + '">' +
              '<span class="p-add">+</span>' +
              thumb +
              '<span class="p-name">' +
                escapeHtml(product.name) +
              '</span>' +
              '<span class="p-stock">Stok ' +
                fmtNum(product.stock) + ' ' +
                escapeHtml(product.unit) +
              '</span>' +
              '<span class="p-price">' +
                fmtRp(product.price) +
              '</span>' +
            '</button>'
          );
        }).join('');
      })
      .catch(function () {
        resultsEl.innerHTML =
          '<div class="alert alert-error" style="grid-column:1/-1">' +
            'Gagal memuat produk. Periksa koneksi, lalu muat ulang halaman.' +
          '</div>';
      });
  }

  function add(id) {
    var product = products[id];
    if (!product) return;

    var line = cart.find(function (item) {
      return item.id === id;
    });

    if (line) {
      if (line.qty < product.stock) {
        line.qty++;
      } else {
        toast(
          'Stok ' + product.name + ' hanya ' + product.stock + '.',
          'error'
        );
      }
    } else {
      cart.push({
        id: id,
        name: product.name,
        unit: product.unit,
        price: product.price,
        stock: product.stock,
        qty: 1
      });
    }

    render();
  }

  // ==========================================================
  // TAMPILAN KERANJANG DAN PEMBAYARAN
  // ==========================================================

  function render() {
    var t = total();

    var count = cart.reduce(function (sum, item) {
      return sum + item.qty;
    }, 0);

    $('cart-count').textContent = count;
    $('cart-mini').textContent = fmtRp(t);
    totalEl.textContent = fmtRp(t);

    if (!cart.length) {
      cartEl.innerHTML =
        '<div class="empty" style="padding:22px 8px">' +
          '<p class="mb-0">Keranjang kosong. Pilih produk di sebelah kiri.</p>' +
        '</div>';
    } else {
      cartEl.innerHTML = cart.map(function (item, index) {
        return (
          '<div class="cart-line">' +
            '<div>' +
              '<div class="c-name">' +
                escapeHtml(item.name) +
              '</div>' +
              '<div class="muted small">' +
                fmtRp(item.price) + ' / ' +
                escapeHtml(item.unit) +
              '</div>' +
            '</div>' +
            '<div class="c-sum">' +
              fmtRp(item.price * item.qty) +
            '</div>' +
            '<div class="qty sm">' +
              '<button type="button" data-act="dec" data-i="' +
                index + '" aria-label="Kurangi">&minus;</button>' +
              '<input type="number" min="1" max="' +
                item.stock + '" value="' + item.qty +
                '" data-act="set" data-i="' + index +
                '" aria-label="Jumlah ' + escapeHtml(item.name) + '" />' +
              '<button type="button" data-act="inc" data-i="' +
                index + '" aria-label="Tambah">+</button>' +
            '</div>' +
            '<button type="button" class="link-btn danger small" ' +
              'data-act="del" data-i="' + index +
              '" style="justify-self:end">Hapus</button>' +
          '</div>'
        );
      }).join('');
    }

    var paymentMethod = method();
    var cash = paymentMethod === 'CASH';
    var snapshot = paymentSnapshot();

    // Kalau barang atau nominal berubah, simulasi lama tidak berlaku.
    if (mockPaidFor && mockPaidFor !== snapshot) {
      mockPaidFor = null;
      setMockStatus('', false);
    }

    cashBox.classList.toggle('hide', !cash);

    if (mockPaymentBox) {
      mockPaymentBox.classList.toggle('hide', cash || !cart.length);
    }

    if (qrisPanel) {
      qrisPanel.classList.toggle(
        'hide',
        cash || paymentMethod !== 'QRIS' || !cart.length
      );
    }

    if (transferPanel) {
      transferPanel.classList.toggle(
        'hide',
        cash || paymentMethod !== 'TRANSFER' || !cart.length
      );
    }

    if (qrisTotalEl) {
      qrisTotalEl.textContent = fmtRp(t);
    }

    if (transferTotalEl) {
      transferTotalEl.textContent = fmtRp(t);
    }

    var mockConfirmed = !cash && mockPaidFor === snapshot;

    if (mockConfirmed) {
      setMockStatus(
        'Pembayaran ' +
          (paymentMethod === 'QRIS' ? 'QRIS' : 'Transfer') +
          ' SIMULASI berhasil. Klik "Selesaikan & cetak struk" ' +
          'untuk mencatat penjualan dan mengurangi stok.',
        true
      );
    } else if (
      paymentMethod === 'QRIS' ||
      paymentMethod === 'TRANSFER'
    ) {
      setMockStatus(
        'Lakukan simulasi pembayaran terlebih dahulu.',
        false
      );
    }

    if (qrisSimulateBtn) {
      qrisSimulateBtn.disabled =
        busy || !cart.length || mockConfirmed;
    }

    if (transferSimulateBtn) {
      transferSimulateBtn.disabled =
        busy || !cart.length || mockConfirmed;
    }

    // Pembayaran tunai dan kembalian.
    var paid = parseFloat(paidEl.value) || 0;

    if (cash) {
      var choices = [
        t,
        roundUp(t, 5000),
        roundUp(t, 10000),
        roundUp(t, 50000),
        roundUp(t, 100000)
      ].filter(function (value, index, arr) {
        return value > 0 && arr.indexOf(value) === index;
      }).slice(0, 4);

      quickEl.innerHTML = choices.map(function (value, index) {
        return (
          '<button type="button" data-cash="' + value + '">' +
            (index === 0 ? 'Uang pas' : fmtRp(value)) +
          '</button>'
        );
      }).join('');

      var diff = paid - t;

      changeBox.classList.toggle(
        'short',
        paid > 0 && diff < 0
      );

      changeBox.firstElementChild.textContent =
        paid > 0 && diff < 0 ? 'Kurang' : 'Kembalian';

      changeEl.textContent = fmtRp(
        paid > 0 ? Math.abs(diff) : 0
      );
    }

    // Tunai boleh dikosongkan untuk dianggap uang pas.
    // QRIS/Transfer wajib disimulasikan dahulu.
    var okCash = !cash || paid === 0 || paid >= t;

    confirmBtn.disabled =
      busy ||
      !cart.length ||
      !okCash ||
      (!cash && !mockConfirmed);
  }

  function roundUp(value, step) {
    return Math.ceil(value / step) * step;
  }

  // ==========================================================
  // KONFIRMASI TRANSAKSI
  // ==========================================================

  function checkout() {
    if (busy || !cart.length) return;

    var cash = method() === 'CASH';
    var paid = parseFloat(paidEl.value);

    if (!cash && mockPaidFor !== paymentSnapshot()) {
      setMockStatus(
        'Selesaikan simulasi pembayaran terlebih dahulu.',
        true
      );
      render();
      return;
    }

    busy = true;
    confirmBtn.disabled = true;

    // Tetap menggunakan endpoint kasir dan SalesService utama.
    fetch(checkoutUrl, {
      method: 'POST',
      credentials: 'same-origin',
      headers: {
        'Content-Type': 'application/json',
        'RequestVerificationToken': token
      },
      body: JSON.stringify({
        lines: cart.map(function (item) {
          return {
            productId: item.id,
            qtyBase: item.qty
          };
        }),
        paymentMethod: method(),
        idempotencyKey: key,
        paidAmount:
          cash && Number.isFinite(paid) && paid > 0
            ? paid
            : null
      })
    })
      .then(function (response) {
        return response.text().then(function (text) {
          var data = null;

          try {
            data = JSON.parse(text);
          } catch (_) {
            data = null;
          }

          return {
            ok: response.ok,
            data: data
          };
        });
      })
      .then(function (result) {
        busy = false;

        if (!result.data) {
          showError(
            'Permintaan ditolak. Muat ulang halaman lalu coba lagi.'
          );
          return;
        }

        if (!result.ok || !result.data.ok) {
          showError(
            result.data.error || 'Transaksi gagal.'
          );
          return;
        }

        // Transaksi sudah disimpan server.
        // Buka struk berdasarkan ID transaksi aktual.
        window.location.href =
          receiptUrl + '/' + result.data.id + '?baru=1';
      })
      .catch(function () {
        busy = false;
        showError('Koneksi bermasalah. Coba lagi.');
      });
  }

  function showError(text) {
    messageEl.innerHTML =
      '<div class="alert alert-error mt-1" style="margin-bottom:0">' +
        escapeHtml(text) +
      '</div>';

    render();
  }

  // ==========================================================
  // EVENT KERANJANG
  // ==========================================================

  resultsEl.addEventListener('click', function (event) {
    var button = event.target.closest('.pos-item');

    if (button) {
      add(parseInt(button.dataset.id, 10));
    }
  });

  cartEl.addEventListener('click', function (event) {
    var button = event.target.closest('[data-act]');

    if (!button || button.tagName === 'INPUT') return;

    var index = parseInt(button.dataset.i, 10);
    var item = cart[index];

    if (!item) return;

    if (button.dataset.act === 'inc') {
      item.qty = Math.min(item.stock, item.qty + 1);
    }

    if (button.dataset.act === 'dec') {
      item.qty--;

      if (item.qty <= 0) {
        cart.splice(index, 1);
      }
    }

    if (button.dataset.act === 'del') {
      cart.splice(index, 1);
    }

    render();
  });

  cartEl.addEventListener('change', function (event) {
    var input = event.target.closest('input[data-act=set]');

    if (!input) return;

    var item = cart[parseInt(input.dataset.i, 10)];

    if (!item) return;

    item.qty = Math.max(
      1,
      Math.min(
        item.stock,
        parseInt(input.value, 10) || 1
      )
    );

    render();
  });

  quickEl.addEventListener('click', function (event) {
    var button = event.target.closest('[data-cash]');

    if (button) {
      paidEl.value = button.dataset.cash;
      render();
    }
  });

  paidEl.addEventListener('input', render);

  document.querySelectorAll('input[name="pos-method"]')
    .forEach(function (radio) {
      radio.addEventListener('change', function () {
        paidEl.value = '';
        mockPaidFor = null;
        setMockStatus('', false);
        render();
      });
    });

  // ==========================================================
  // QRIS DAN TRANSFER DUMMY
  // ==========================================================

  function simulateNonCash(expectedMethod) {
    if (
      busy ||
      !cart.length ||
      method() !== expectedMethod
    ) {
      return;
    }

    // Menandai simulasi sukses hanya untuk isi keranjang saat ini.
    mockPaidFor = paymentSnapshot();
    render();
  }

  if (qrisSimulateBtn) {
    qrisSimulateBtn.addEventListener('click', function () {
      simulateNonCash('QRIS');
    });
  }

  if (transferSimulateBtn) {
    transferSimulateBtn.addEventListener('click', function () {
      simulateNonCash('TRANSFER');
    });
  }

  if (copyTransferBtn) {
    copyTransferBtn.addEventListener('click', function () {
      var accountText =
        'BANK DEMO (FIKTIF)\n' +
        'Nomor rekening: 0000000000\n' +
        'Atas nama: WARUNG MAJU - DUMMY\n' +
        'HANYA UNTUK SIMULASI';

      if (
        navigator.clipboard &&
        navigator.clipboard.writeText
      ) {
        navigator.clipboard.writeText(accountText)
          .then(function () {
            setMockStatus(
              'Detail rekening dummy berhasil disalin.',
              true
            );
          })
          .catch(function () {
            setMockStatus(accountText, true);
          });
      } else {
        setMockStatus(accountText, true);
      }
    });
  }

  // ==========================================================
  // EVENT UMUM
  // ==========================================================

  confirmBtn.addEventListener('click', checkout);

  searchBox.addEventListener('input', function () {
    clearTimeout(timer);
    timer = setTimeout(loadProducts, 220);
  });

  var head = $('cart-head');

  if (head) {
    head.addEventListener('click', function () {
      if (window.matchMedia('(max-width: 960px)').matches) {
        cartEl2.classList.toggle('open');
      }
    });
  }

  loadProducts();
  render();
})();
