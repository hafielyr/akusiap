// Alpine component for the quiz player (SES-03, SES-04). Every answer or flag change is saved at once (in order) to
// POST /sesi/{id}/jawab; a 409 means the session has ended. The current question is remembered in this browser only.
// Loaded as a plain script at the end of the page, so it is defined before the deferred Alpine starts.
window.quizPlayer = function (stateElementId) {
  const element = document.getElementById(stateElementId);
  const state = JSON.parse(element.textContent);
  const pad = (n) => String(n).padStart(2, '0');
  const positionKey = `akusiap:attempt:${state.attemptId}:index`;
  const storage = {
    get() { try { return Number(localStorage.getItem(positionKey)); } catch { return NaN; } },
    set(value) { try { localStorage.setItem(positionKey, String(value)); } catch { /* private mode */ } },
  };

  return {
    items: state.items.map((item) => ({ ...item, selected: [...item.selected] })),
    index: 0,
    secondsLeft: state.secondsLeft,
    ended: false,
    confirming: false,
    saveFailed: false,
    queue: Promise.resolve(),
    enteredAt: Date.now(),

    init() {
      const saved = storage.get();
      const firstOpen = this.items.findIndex((i) => i.selected.length === 0);
      this.index = Number.isInteger(saved) && saved >= 0 && saved < this.items.length ? saved : Math.max(0, firstOpen);
      if (this.secondsLeft !== null) {
        const timer = setInterval(() => {
          if (this.secondsLeft > 0) {
            this.secondsLeft--;
          } else {
            // Time is up: save what is pending, then submit (the server closes it anyway, D-09).
            this.ended = true;
            clearInterval(timer);
            this.submit();
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
      if (this.ended) return;
      const item = this.item;
      if (this.isSingle(item)) {
        item.selected = [id];
      } else {
        item.selected = item.selected.includes(id) ? item.selected.filter((x) => x !== id) : [...item.selected, id];
      }
      this.save(item);
    },

    toggleFlag() {
      if (this.ended) return;
      this.item.flagged = !this.item.flagged;
      this.save(this.item);
    },

    // Seconds on the current question since it was opened or last saved.
    takeSeconds() {
      const now = Date.now();
      const seconds = Math.round((now - this.enteredAt) / 1000);
      this.enteredAt = now;
      return seconds;
    },

    save(item) {
      const body = new URLSearchParams();
      body.append('position', item.position);
      body.append('flagged', item.flagged);
      body.append('seconds', this.takeSeconds());
      item.selected.forEach((id) => body.append('selected', id));
      this.queue = this.queue
        .then(() => fetch(state.saveUrl, { method: 'POST', body, headers: { RequestVerificationToken: state.token } }))
        .then((response) => {
          if (response.status === 409) {
            this.ended = true;
          }
          this.saveFailed = !response.ok && response.status !== 409;
        })
        .catch(() => { this.saveFailed = true; });
    },

    // Sends the attempt after any saves still in flight.
    submit() {
      this.queue.then(() => this.$refs.submitForm.submit());
    },

    go(i) {
      if (i < 0 || i >= this.items.length) return;
      this.index = i;
      this.enteredAt = Date.now();
      storage.set(i);
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
