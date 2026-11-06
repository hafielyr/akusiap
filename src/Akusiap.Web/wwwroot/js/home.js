// Home page motion (BRD-09, D-116). Sections rise into place once as they enter the screen, and the tagline lights up
// one word at a time in reading order as it scrolls past. Content stays fully visible until this script adds the
// "motion" class, so nothing is hidden if the script never runs. Reduced motion shows everything at once.
(() => {
  const root = document.documentElement;
  const reduce = matchMedia('(prefers-reduced-motion: reduce)').matches;
  const words = [...document.querySelectorAll('.tagline-word')];
  const showAll = () => {
    document.querySelectorAll('.reveal').forEach((el) => el.classList.add('is-in'));
    words.forEach((w) => w.classList.add('is-on'));
  };
  if (!('IntersectionObserver' in window)) return;
  root.classList.add('motion');
  if (reduce) { showAll(); return; }

  const reveal = new IntersectionObserver((entries) => {
    for (const entry of entries) {
      if (entry.isIntersecting) { entry.target.classList.add('is-in'); reveal.unobserve(entry.target); }
    }
  }, { rootMargin: '0px 0px -10% 0px' });
  document.querySelectorAll('.reveal').forEach((el) => reveal.observe(el));

  if (!words.length) return;
  const block = words[0].parentElement.parentElement;
  let lit = 0;
  let queued = false;
  let listening = false;
  // Progress runs from 0 when the tagline's top reaches 85% of the screen to 1 at 35%; that many words are lit.
  const update = () => {
    queued = false;
    const top = block.getBoundingClientRect().top;
    const start = innerHeight * 0.85;
    const end = innerHeight * 0.35;
    const progress = Math.min(1, Math.max(0, (start - top) / (start - end)));
    const target = Math.round(progress * words.length);
    while (lit < target) words[lit++].classList.add('is-on');
    if (lit >= words.length) { stop(); watch.disconnect(); }
  };
  const onScroll = () => { if (!queued) { queued = true; requestAnimationFrame(update); } };
  const start = () => { if (!listening) { listening = true; addEventListener('scroll', onScroll, { passive: true }); update(); } };
  const stop = () => { if (listening) { listening = false; removeEventListener('scroll', onScroll); } };
  // Listen to scrolling only while the tagline is near the screen.
  const watch = new IntersectionObserver(([entry]) => (entry.isIntersecting ? start() : stop()));
  watch.observe(block);
})();
