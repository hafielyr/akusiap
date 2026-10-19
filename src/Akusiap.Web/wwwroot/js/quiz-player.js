// Alpine component for the quiz player (SES-03). It keeps the answers, flags and position in the page; SES-04 adds
// saving. Loaded as a plain script at the end of the page, so it is defined before the deferred Alpine starts.
window.quizPlayer = function (stateElementId) {
  const state = JSON.parse(document.getElementById(stateElementId).textContent);
  const pad = (n) => String(n).padStart(2, '0');

  return {
    items: state.items.map((item) => ({ ...item, selected: [...item.selected] })),
    index: 0,
    secondsLeft: state.secondsLeft,

    init() {
      if (this.secondsLeft !== null) {
        const timer = setInterval(() => {
          if (this.secondsLeft > 0) {
            this.secondsLeft--;
          } else {
            clearInterval(timer);
          }
        }, 1000);
      }
    },

    get item() { return this.items[this.index]; },
    get remaining() { return this.items.filter((i) => i.selected.length === 0).length; },
    get isFirst() { return this.index === 0; },
    get isLast() { return this.index === this.items.length - 1; },
    get clock() {
      const s = Math.max(0, this.secondsLeft ?? 0);
      const h = Math.floor(s / 3600);
      const m = Math.floor((s % 3600) / 60);
      return h > 0 ? `${h}:${pad(m)}:${pad(s % 60)}` : `${pad(m)}:${pad(s % 60)}`;
    },

    isSingle(item) { return item.type !== 'MultiSelect'; },
    isSelected(id) { return this.item.selected.includes(id); },

    choose(id) {
      const item = this.item;
      if (this.isSingle(item)) {
        item.selected = [id];
      } else {
        item.selected = item.selected.includes(id) ? item.selected.filter((x) => x !== id) : [...item.selected, id];
      }
    },

    toggleFlag() { this.item.flagged = !this.item.flagged; },

    go(i) {
      if (i < 0 || i >= this.items.length) return;
      this.index = i;
      this.$nextTick(() => this.$refs.question && this.$refs.question.focus());
    },
    next() { this.go(this.index + 1); },
    prev() { this.go(this.index - 1); },

    // Arrow keys move between questions, except inside the options (radios use them to change the answer).
    onKey(event) {
      if (event.target.closest('input, select, textarea')) return;
      if (event.key === 'ArrowRight') { this.next(); event.preventDefault(); }
      if (event.key === 'ArrowLeft') { this.prev(); event.preventDefault(); }
    },

    cellClass(i) {
      const item = this.items[i];
      return [
        item.selected.length ? 'bg-brand-600 text-white border-brand-600' : 'bg-white text-slate-700 border-slate-300',
        item.flagged ? 'ring-2 ring-amber-500 ring-offset-1' : '',
        i === this.index ? 'outline-2 outline-offset-2 outline-slate-900' : '',
      ].join(' ');
    },
  };
};
