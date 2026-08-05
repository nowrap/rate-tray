// Motion on this page is the product's logic, never decoration: values climb to their readings
// and cross the thresholds on the way, and the details window refreshes itself the way the real
// one does. Nothing moves unless it is the thing being looked at.
(function () {
  var WARN = 75, CRIT = 90, SPREAD = 0.15;

  var german = (document.documentElement.lang || 'en').slice(0, 2) === 'de';
  var UNIT = german ? 'Prozent' : 'per cent';

  // --------------------------------------------------------------- colour --
  // Read per block rather than from :root. The specimens are always dark whatever the page
  // theme is, and they declare the app's dark palette on themselves — asking the document
  // would paint light-theme colours onto a dark taskbar.
  function paletteOf(el) {
    var css = getComputedStyle(el);
    return {
      claude: css.getPropertyValue('--claude').trim(),
      codex: css.getPropertyValue('--codex').trim(),
      warn: css.getPropertyValue('--warn').trim(),
      crit: css.getPropertyValue('--crit').trim()
    };
  }

  function parse(colour) {
    if (colour.charAt(0) === '#') {
      var n = parseInt(colour.slice(1), 16);
      return [(n >> 16) & 255, (n >> 8) & 255, n & 255];
    }
    return colour.match(/\d+/g).slice(0, 3).map(Number);
  }

  function toHsl(rgb) {
    var r = rgb[0] / 255, g = rgb[1] / 255, b = rgb[2] / 255;
    var max = Math.max(r, g, b), min = Math.min(r, g, b), d = max - min;
    var l = (max + min) / 2, h = 0, s = 0;
    if (d) {
      s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
      if (max === r) h = ((g - b) / d + (g < b ? 6 : 0)) / 6;
      else if (max === g) h = ((b - r) / d + 2) / 6;
      else h = ((r - g) / d + 4) / 6;
    }
    return [h, s, l];
  }

  function fromHsl(h, s, l) {
    if (!s) { var v = Math.round(l * 255); return 'rgb(' + v + ',' + v + ',' + v + ')'; }
    var q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
    function channel(t) {
      if (t < 0) t += 1;
      if (t > 1) t -= 1;
      if (t < 1 / 6) return p + (q - p) * 6 * t;
      if (t < 1 / 2) return q;
      if (t < 2 / 3) return p + (q - p) * (2 / 3 - t) * 6;
      return p;
    }
    return 'rgb(' + [channel(h + 1 / 3), channel(h), channel(h - 1 / 3)]
      .map(function (c) { return Math.round(c * 255); }).join(',') + ')';
  }

  // Same shading the app applies between limits of one service: lightness only, so the
  // brand hue survives.
  function shade(colour, step) {
    if (!step) return colour;
    var lift = step * SPREAD * 255;
    return 'rgb(' + parse(colour)
      .map(function (c) { return Math.min(255, Math.round(c + lift)); }).join(',') + ')';
  }

  // Palette.Track: same hue, a quarter of the saturation, very dark. An amber bar therefore
  // gets an amber-dark track, exactly as in the app.
  function trackFor(colour) {
    var hsl = toHsl(parse(colour));
    return fromHsl(hsl[0], hsl[1] * 0.28, 0.19);
  }

  function colourFor(el, value, pal) {
    if (value >= CRIT) return pal.crit;
    if (value >= WARN) return pal.warn;
    return shade(pal[el.dataset.service], +el.dataset.shade / 2);
  }

  var reduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

  // Count from zero to the reading, easing out. A callback returning false stops the run,
  // which is how a replayed block abandons the frames of its previous one.
  function climb(from, to, duration, delay, onFrame) {
    var started = null;
    function step(now) {
      if (started === null) started = now;
      var t = Math.min(1, Math.max(0, (now - started - delay) / duration));
      var eased = 1 - Math.pow(1 - t, 3);
      if (onFrame(from + (to - from) * eased, t === 1) === false) return;
      if (t < 1) requestAnimationFrame(step);
    }
    requestAnimationFrame(step);
  }

  // Linear, for anything that represents time passing rather than a value settling.
  function sweep(duration, onFrame) {
    var started = null;
    function step(now) {
      if (started === null) started = now;
      var t = Math.min(1, (now - started) / duration);
      if (onFrame(t) === false) return;
      if (t < 1) requestAnimationFrame(step);
    }
    requestAnimationFrame(step);
  }

  // ------------------------------------------------------------- scheduling --
  // Animation follows attention: a block plays once scrolling has settled and that block is
  // the one nearest the middle of the window, so only one thing is ever moving and it is the
  // thing being looked at. Leaving a block stops it and arms it for the next visit; nudging
  // the page while it is already in focus changes nothing. FIRE and ARM differ on purpose,
  // so a block near the edge of the band cannot flicker between the two states.
  var FIRE = 0.30, ARM = 0.55, SETTLED = 160;
  var blocks = [];

  // Every block paints its settled state before registering here, so reduced motion simply
  // never registers: no listeners, no frames, and — the one that would actually have hurt —
  // no self-restarting poll loop in the details window.
  function play(el, run, stop) {
    if (!el || reduced) return;
    blocks.push({ el: el, run: run, stop: stop, armed: true });
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
      if (b !== nearest) { leave(b); return; }
      // On load the nearest visible block plays wherever it sits: a tall hero can push the
      // specimen out of the band, and a page that opens on dead zeros is worse than one that
      // animates something slightly off centre.
      if (!force && best > ARM * window.innerHeight) { leave(b); return; }
      if (b.armed && (force || best <= FIRE * window.innerHeight)) {
        b.armed = false;
        b.run();
      }
    });
  }

  function leave(b) {
    if (!b.armed && b.stop) b.stop();
    b.armed = true;
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

    settle(true);        // whatever is already on screen plays without waiting to be scrolled to
  }

  // ------------------------------------------------------------------ tray --
  var specimen = document.querySelector('.specimen');
  var icons = Array.prototype.slice.call(document.querySelectorAll('#tray .tray-icon'));

  if (specimen && icons.length) {
    var trayPalette = paletteOf(specimen);

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

    var showCard = function (el) {
      var value = +el.textContent;
      var colour = colourFor(el, value, trayPalette);

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
    };

    var active = icons[1];
    icons.forEach(function (el) {
      el.setAttribute('aria-label', el.dataset.name + ', ' + el.dataset.target + ' ' + UNIT + '. ' + el.dataset.reset);
      ['mouseenter', 'focus'].forEach(function (event) {
        el.addEventListener(event, function () { active = el; showCard(el); });
      });
    });
    window.addEventListener('resize', function () { showCard(active); });

    var paintIcon = function (el, value) {
      el.textContent = Math.round(value);
      el.style.color = colourFor(el, +el.textContent, trayPalette);
      if (el === active) showCard(el);         // keep the card in step while the value climbs
    };

    // Settled state first. A block only animates once it is the one being looked at, so
    // anything scrolled past quickly must already read correctly rather than showing zeros.
    icons.forEach(function (el) { paintIcon(el, +el.dataset.target); });

    var trayRun = 0;
    play(specimen, function () {
      var mine = ++trayRun;
      icons.forEach(function (el, i) {
        climb(0, +el.dataset.target, 1500, i * 90, function (value) {
          if (mine !== trayRun) return false;
          paintIcon(el, value);
        });
      });
    }, function () {
      trayRun++;                               // abandon the frames, then settle where they were headed
      icons.forEach(function (el) { paintIcon(el, +el.dataset.target); });
    });
  }

  // ----------------------------------------------------------------- rules --
  // The same climb on the bars the details window draws, so the reader watches 81 turn amber
  // and 93 turn crimson instead of taking the rule on trust.
  var meters = Array.prototype.slice.call(document.querySelectorAll('.rule .meter'));

  if (meters.length) {
    var paintMeter = function (meter, value) {
      // The page palette here, not a specimen's: these bars sit on the page and follow its theme.
      var colour = colourFor(meter, Math.round(value), paletteOf(document.documentElement));
      var num = meter.querySelector('.num');
      num.textContent = Math.round(value);
      num.style.color = colour;
      var fill = meter.querySelector('.track > span');
      fill.style.width = value + '%';
      fill.style.background = colour;
    };

    meters.forEach(function (meter) {
      meter.setAttribute('aria-label', Math.round(+meter.dataset.target) + ' ' + UNIT);
      paintMeter(meter, +meter.dataset.target);
    });

    var rulesRun = 0;
    play(meters[0].closest('.rules'), function () {
      var mine = ++rulesRun;
      meters.forEach(function (meter, i) {
        climb(0, +meter.dataset.target, 1400, i * 120, function (value) {
          if (mine !== rulesRun) return false;
          paintMeter(meter, value);
        });
      });
    }, function () {
      rulesRun++;
      meters.forEach(function (meter) { paintMeter(meter, +meter.dataset.target); });
    });
  }

  // ------------------------------------------------------- details window --
  // A reproduction rather than a screenshot, because the one thing a screenshot cannot show
  // is the part that matters: the window refreshes itself. The strip along the bottom is the
  // real countdown to the next poll, and each time it runs out the readings step up and the
  // timestamp advances by the refresh interval.
  var pane = document.getElementById('details');

  if (pane) {
    var rows = Array.prototype.slice.call(pane.querySelectorAll('.limit'));
    var strip = pane.querySelector('.strip > span');
    var stampNode = pane.querySelector('.stamp');
    var panePalette = paletteOf(pane);

    // Three polls' worth of a working afternoon. The weekly limit crosses 90 on the last one,
    // which is the state the page spends its colour section explaining.
    var POLLS = [
      { at: '20:37:42', values: [34, 81, 22, 61] },
      { at: '20:39:12', values: [37, 84, 22, 64] },
      { at: '20:42:12', values: [41, 91, 24, 68] }
    ];
    var COUNTDOWN = 3600, FIRST = 1400, STEP = 700;

    var detailsRun = 0;

    function paint(row, value) {
      var colour = colourFor(row, value, panePalette);
      row.querySelector('.pct').textContent = Math.round(value) + ' %';
      row.querySelector('.pct').style.color = colour;
      var fill = row.querySelector('.track > span');
      fill.style.width = Math.max(0, Math.min(100, value)) + '%';
      fill.style.background = colour;
      row.querySelector('.track').style.background = trackFor(colour);
    }

    function poll(index, from, mine) {
      if (mine !== detailsRun) return;
      var reading = POLLS[index];
      stampNode.textContent = reading.at;

      rows.forEach(function (row, i) {
        climb(from ? from[i] : 0, reading.values[i], from ? STEP : FIRST, i * 90, function (value) {
          if (mine !== detailsRun) return false;
          paint(row, value);
        });
      });

      // The strip fills towards the next poll rather than draining away from the last one —
      // that is the direction DetailsForm.CountdownProgress draws it.
      sweep(COUNTDOWN, function (t) {
        if (mine !== detailsRun) return false;
        strip.style.width = t * 100 + '%';
        if (t < 1) return;
        var next = (index + 1) % POLLS.length;
        // Wrapping replays the whole sequence from zero. Stepping straight back to lower
        // readings would say usage went down, which is not what happened.
        poll(next, next ? reading.values : null, mine);
      });
    }

    function rest() {
      rows.forEach(function (row, i) { paint(row, POLLS[0].values[i]); });
      stampNode.textContent = POLLS[0].at;
      strip.style.width = 0;
    }

    rest();
    play(pane, function () { poll(0, null, ++detailsRun); }, function () { detailsRun++; rest(); });
  }

  watchScrolling();
})();
