// Alpine state for the admin question editor (ADM-T2): add, remove, reorder and mark answer options.
// Loaded as a plain (non-deferred) script inside the page, so it is defined before Alpine starts.
// The server re-validates everything (QuestionValidator); this only keeps the form convenient.
window.questionForm = function (initial) {
  let nextKey = 0;
  const option = (o) => ({ key: nextKey++, id: o.id ?? null, text: o.text ?? '', isCorrect: !!o.isCorrect });
  const trueFalse = () => [
    option({ text: initial.trueText, isCorrect: true }),
    option({ text: initial.falseText, isCorrect: false }),
  ];

  return {
    type: initial.type,
    options: initial.options.map(option),
    saved: null,

    get isTrueFalse() { return this.type === 'TrueFalse'; },
    get singleAnswer() { return this.type !== 'MultiSelect'; },
    get maxOptions() { return initial.maxOptions[this.type] ?? 2; },
    get canAdd() { return !this.isTrueFalse && this.options.length < this.maxOptions; },

    letter(i) { return String.fromCharCode(65 + i); },

    setType(type) {
      const wasTrueFalse = this.isTrueFalse;
      this.type = type;
      if (this.isTrueFalse && !wasTrueFalse) {
        this.saved = this.options;
        this.options = trueFalse();
      } else if (wasTrueFalse && !this.isTrueFalse) {
        this.options = this.saved ?? [option({}), option({}), option({}), option({})];
        this.saved = null;
      }
      if (this.singleAnswer) {
        let seen = false;
        this.options.forEach((o) => { if (o.isCorrect) { o.isCorrect = !seen; seen = true; } });
      }
    },

    add() {
      if (this.canAdd) this.options.push(option({}));
    },

    remove(i) {
      this.options.splice(i, 1);
    },

    move(i, step) {
      const j = i + step;
      if (j < 0 || j >= this.options.length) return;
      [this.options[i], this.options[j]] = [this.options[j], this.options[i]];
    },

    markCorrect(i, checked) {
      if (this.singleAnswer && checked) {
        this.options.forEach((o, k) => { o.isCorrect = k === i; });
      } else {
        this.options[i].isCorrect = checked;
      }
    },
  };
};
