// No inline scripts or handlers anywhere: the Content Security Policy only allows scripts served from this site.

// Select the whole value when a read-only field gets focus (the one-time API key).
document.addEventListener('focusin', function (e) {
  var el = e.target;
  if (el instanceof HTMLInputElement && el.hasAttribute('data-select-on-focus')) {
    el.select();
  }
});

// Covers are requested at a smaller size from RAWG's CDN (see ImageUrls.cs). If that address fails, fall back to the
// original once. 'error' does not bubble, so listen in the capture phase.
document.addEventListener('error', function (e) {
  var el = e.target;
  if (el instanceof HTMLImageElement && el.dataset.fallbackSrc) {
    var original = el.dataset.fallbackSrc;
    delete el.dataset.fallbackSrc; // only once, so a bad original cannot loop
    el.src = original;
  }
}, true);
