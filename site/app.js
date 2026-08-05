// Two moments of motion, and both are the product's logic rather than decoration: values climb
// to their readings and cross the thresholds on the way, so the colour rules explain themselves.
(function () {
  var WARN = 75, CRIT = 90, SPREAD = 0.15;

  var german = (document.documentElement.lang || 'en').slice(0, 2) === 'de';
  var UNIT = german ? 'Prozent' : 'per cent';

  var css = getComputedStyle(document.documentElement);
  var base = {
    claude: css.getPropertyValue('--claude').trim(),
    codex: css.getPropertyValue('--codex').trim(),
    warn: css.getPropertyValue('--warn').trim(),
    crit: css.getPropertyValue('--crit').trim()
  };

  // Same shading the app applies: lightness only, so the brand hue survives.
  function shade(hex, step) {
    if (!step) return hex;
    var n = parseInt(hex.slice(1), 16);
    var r = (n >> 16) & 255, g = (n >> 8) & 255, b = n & 255;
    var lift = step * SPREAD * 255;
    var up = function (c) { return Math.min(255, Math.round(c + lift)); };
    return 'rgb(' + up(r) + ',' + up(g) + ',' + up(b) + ')';
  }

  function colourFor(el, value) {
    if (value >= CRIT) return base.crit;
    if (value >= WARN) return base.warn;
    return shade(base[el.dataset.service], +el.dataset.shade / 2);
  }

  var reduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

  // Count from zero to the reading, easing out, calling back on every frame.
  function climb(target, duration, delay, onFrame) {
    if (reduced) { onFrame(target, true); return; }
    var started = null;
    function step(now) {
      if (started === null) started = now;
      var t = Math.min(1, Math.max(0, (now - started - delay) / duration));
      onFrame(target * (1 - Math.pow(1 - t, 3)), t === 1);
      if (t < 1) requestAnimationFrame(step);
    }
    requestAnimationFrame(step);
  }

  // ------------------------------------------------------------- scheduling --
  // Animation follows attention rather than page load. A block plays when scrolling has
  // settled and that block is the one nearest the middle of the window, so only one thing
  // is ever moving and it is the thing being looked at. Coming back to a block replays it;
  // nudging the page while it is already in focus does not. FIRE and ARM differ on purpose,
  // so a block sitting near the edge of the band cannot flicker between the two states.
  var FIRE = 0.30, ARM = 0.55, SETTLED = 160;
  var blocks = [];

  function play(el, run) {
    if (reduced) { run(); return; }
    blocks.push({ el: el, run: run, armed: true });
  }

  function settle(force) {
    var middle = window.innerHeight / 2;
    var nearest = null, best = Infinity;

    blocks.forEach(function (b) {
      var box = b.el.getBoundingClientRect();
      if (box.bottom < 0 || box.top > window.innerHeight) return;     // off screen entirely
      var distance = Math.abs((box.top + box.bottom) / 2 - middle);
      if (distance < best) { best = distance; nearest = b; }
    });

    blocks.forEach(function (b) {
      if (b !== nearest) { b.armed = true; return; }
      // On load the nearest visible block plays wherever it sits: a tall hero can push the
      // specimen out of the band, and a page that opens on dead zeros is worse than one that
      // animates something slightly off centre.
      if (!force && best > ARM * window.innerHeight) { b.armed = true; return; }
      if (b.armed && (force || best <= FIRE * window.innerHeight)) {
        b.armed = false;
        b.run();
      }
    });
  }

  function watchScrolling() {
    if (reduced || !blocks.length) return;

    var timer = null;
    function pause() {
      clearTimeout(timer);
      timer = setTimeout(function () { settle(false); }, SETTLED);
    }
    ['scroll', 'resize'].forEach(function (event) {
      window.addEventListener(event, pause, { passive: true });
    });

    settle(true);        // the block already on screen plays without waiting to be scrolled to
  }

  // ------------------------------------------------------------------ tray --
  var icons = Array.prototype.slice.call(document.querySelectorAll('#tray .tray-icon'));

  // The card starts on the amber icon rather than waiting to be discovered: most
  // visitors never hover anything, and it is the state worth seeing.
  var card = document.getElementById('card');
  var stage = card.parentNode;
  var marks = {
    claude: '<svg width="10" height="10" viewBox="0 0 10 10" aria-hidden="true">' +
            '<path d="M5 0 C5.4 3.4 6.6 4.6 10 5 C6.6 5.4 5.4 6.6 5 10 C4.6 6.6 3.4 5.4 0 5 C3.4 4.6 4.6 3.4 5 0Z" fill="CURRENT"/></svg>',
    codex:  '<svg width="10" height="10" viewBox="0 0 10 10" fill="none" stroke="CURRENT" stroke-width="1.4" ' +
            'stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' +
            '<path d="M1 1.4 4 5 1 8.6"/><path d="M6 9h3"/></svg>'
  };

  function showCard(el) {
    var value = +el.textContent;
    var colour = colourFor(el, value);

    document.getElementById('card-mark').innerHTML = marks[el.dataset.service].replace(/CURRENT/g, colour);
    document.getElementById('card-mark').style.background = 'rgba(255,255,255,0.06)';
    document.getElementById('card-name').textContent = el.dataset.name;
    document.getElementById('card-reset').textContent = el.dataset.reset;

    var valueNode = document.getElementById('card-value');
    valueNode.textContent = value + ' %';
    valueNode.style.color = colour;

    // Centred on the icon, then kept inside the strip so it never hangs off an edge.
    var iconBox = el.getBoundingClientRect();
    var stageBox = stage.getBoundingClientRect();
    var left = iconBox.left - stageBox.left + iconBox.width / 2 - card.offsetWidth / 2;
    card.style.left = Math.max(10, Math.min(left, stageBox.width - card.offsetWidth - 10)) + 'px';
  }

  var active = icons[1];
  icons.forEach(function (el) {
    el.setAttribute('aria-label', el.dataset.name + ', ' + el.dataset.target + ' ' + UNIT + '. ' + el.dataset.reset);
    ['mouseenter', 'focus'].forEach(function (event) {
      el.addEventListener(event, function () { active = el; showCard(el); });
    });
  });
  window.addEventListener('resize', function () { showCard(active); });

  play(document.querySelector('.specimen'), function () {
    icons.forEach(function (el, i) {
      climb(+el.dataset.target, 1500, i * 90, function (value) {
        el.textContent = Math.round(value);
        el.style.color = colourFor(el, +el.textContent);
        if (el === active) showCard(el);        // keep the card in step while the value climbs
      });
    });
  });

  // ----------------------------------------------------------------- rules --
  // The same climb on the bars the details window draws, so the reader watches 81 turn
  // amber and 93 turn crimson instead of taking the rule on trust. Held until the section
  // is actually on screen — a threshold crossing nobody sees teaches nobody anything.
  var meters = Array.prototype.slice.call(document.querySelectorAll('.rule .meter'));

  function runMeters() {
    meters.forEach(function (meter, i) {
      var target = +meter.dataset.target;
      var num = meter.querySelector('.num');
      var fill = meter.querySelector('.track > span');

      meter.setAttribute('aria-label', Math.round(target) + ' ' + UNIT);
      climb(target, 1400, i * 120, function (value) {
        var shown = Math.round(value);
        var colour = colourFor(meter, shown);
        num.textContent = shown;
        num.style.color = colour;
        fill.style.width = value + '%';
        fill.style.background = colour;
      });
    });
  }

  if (meters.length) play(meters[0].closest('.rules'), runMeters);

  watchScrolling();
})();
