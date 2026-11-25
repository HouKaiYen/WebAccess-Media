function normalizeMarqueeText(value) {
    return value.replace(/\r\n?/g, '\n').replace(/^\p{White_Space}+|\p{White_Space}+$/gu, '');
}

function setupMarqueeCharacterEditor(textInput, readSettings, markDirty) {
    const hiddenStyles = document.getElementById('MarqueeCharacterStyles');
    const hint = document.getElementById('marquee-character-hint');
    if (typeof Intl.Segmenter !== 'function') {
        hint.textContent = '請用支援逐字樣式的新版瀏覽器編輯。';
        const initialText = normalizeMarqueeText(textInput.value);
        textInput.addEventListener('input', () => {
            if (normalizeMarqueeText(textInput.value) !== initialText) hiddenStyles.value = '[]';
        });
        return;
    }

    const segmenter = new Intl.Segmenter('zh-Hant', { granularity: 'grapheme' });
    const list = document.getElementById('marquee-character-list');
    const controls = document.getElementById('marquee-character-controls');
    const selectedCharacter = document.getElementById('marquee-selected-character');
    const color = document.getElementById('marquee-character-color');
    const resetColor = document.getElementById('reset-marquee-character-color');
    const size = document.getElementById('marquee-character-size');
    const weight = document.getElementById('marquee-character-weight');
    const sample = document.getElementById('marquee-character-sample');
    const defaultHint = hint.textContent;
    let text = normalizeMarqueeText(textInput.value);
    let characters = splitCharacters(text);
    let selectedIndex = 0;
    let edit = null;

    function splitCharacters(value) {
        return Array.from(segmenter.segment(value), ({ segment, index }) => ({ segment, index, style: {} }));
    }

    function characterLabel(segment) {
        if (segment === '\n') return '換行';
        return /^\p{White_Space}+$/u.test(segment) ? '空白' : segment;
    }

    function hasStyle(character) {
        return Object.keys(character.style).length > 0;
    }

    function updateButton(button, index) {
        const selected = index === selectedIndex;
        const character = characters[index];
        const styled = hasStyle(character);
        button.tabIndex = selected ? 0 : -1;
        button.setAttribute('aria-pressed', String(selected));
        button.toggleAttribute('data-styled', styled);
        button.setAttribute('aria-label', '第 ' + (index + 1) + ' 字 ' + characterLabel(character.segment) + (styled ? '，有自訂樣式' : ''));
    }

    function showSelection() {
        const character = characters[selectedIndex];
        if (!character) return;
        selectedCharacter.textContent = '第 ' + (selectedIndex + 1) + ' 字「' + characterLabel(character.segment) + '」';
        color.value = character.style.textColor || document.getElementById('MarqueeTextColor').value;
        size.value = character.style.fontSize ?? '';
        weight.value = character.style.fontWeight ?? '';
        resetColor.disabled = !character.style.textColor;
    }

    function updateSample() {
        const settings = readSettings();
        applyMarqueeAppearance(sample, settings);
        renderMarqueeText(sample, settings.marquee, settings.marqueeCharacterStyles);
    }

    function writeStyles() {
        const styles = characters.filter(hasStyle).map(character => ({
            start: character.index, length: character.segment.length, ...character.style
        }));
        hiddenStyles.value = JSON.stringify(styles);
        updateSample();
    }

    function renderButtons() {
        list.replaceChildren(...characters.map((character, index) => {
            const button = document.createElement('button');
            button.type = 'button';
            button.dataset.index = index;
            button.textContent = characterLabel(character.segment);
            updateButton(button, index);
            return button;
        }));
        list.hidden = controls.hidden = characters.length === 0;
        hint.textContent = characters.length ? defaultHint : '輸入公告文字後即可逐字編輯。';
        showSelection();
    }

    function selectCharacter(index) {
        const previous = selectedIndex;
        selectedIndex = index;
        updateButton(list.children[previous], previous);
        updateButton(list.children[index], index);
        showSelection();
    }

    function changeStyle(property, value) {
        const style = characters[selectedIndex].style;
        if (value === null) delete style[property];
        else style[property] = value;
        updateButton(list.children[selectedIndex], selectedIndex);
        resetColor.disabled = !style.textColor;
        writeStyles();
        markDirty();
    }

    const savedStyles = new Map((readSettings().marqueeCharacterStyles ?? []).map(style => [style.start, style]));
    for (const character of characters) {
        const saved = savedStyles.get(character.index);
        if (!saved || saved.length !== character.segment.length) continue;
        for (const property of ['textColor', 'fontSize', 'fontWeight']) {
            if (saved[property] != null) character.style[property] = saved[property];
        }
    }
    renderButtons();
    updateSample();

    list.addEventListener('click', event => {
        const button = event.target.closest('button[data-index]');
        if (button) selectCharacter(Number(button.dataset.index));
    });
    list.addEventListener('keydown', event => {
        const offsets = { ArrowLeft: selectedIndex - 1, ArrowRight: selectedIndex + 1, Home: 0, End: characters.length - 1 };
        if (!Object.hasOwn(offsets, event.key)) return;
        event.preventDefault();
        const index = Math.max(0, Math.min(characters.length - 1, offsets[event.key]));
        selectCharacter(index);
        list.children[index].focus();
    });
    color.addEventListener('input', () => changeStyle('textColor', color.value));
    resetColor.addEventListener('click', () => {
        changeStyle('textColor', null);
        showSelection();
        color.focus();
    });
    size.addEventListener('input', () => {
        if (size.validity.valid) changeStyle('fontSize', size.value ? size.valueAsNumber : null);
    });
    weight.addEventListener('change', () => changeStyle('fontWeight', weight.value ? Number(weight.value) : null));
    textInput.form.addEventListener('input', event => {
        if (!['MarqueeTextColor', 'MarqueeBackgroundColor', 'MarqueeBackgroundOpacity', 'MarqueeFontWeight', 'MarqueeFontSize'].includes(event.target.id)) return;
        if (characters.length && event.target.id === 'MarqueeTextColor') {
            color.value = characters[selectedIndex].style.textColor || event.target.value;
        }
        updateSample();
    });

    textInput.addEventListener('beforeinput', event => {
        edit = { value: textInput.value, start: textInput.selectionStart, end: textInput.selectionEnd, type: event.inputType };
    });
    textInput.addEventListener('input', () => {
        const nextText = normalizeMarqueeText(textInput.value);
        const editDetails = edit;
        edit = null;
        if (nextText === text) return;

        const next = splitCharacters(nextText);
        let untouchedEnd = Infinity;
        if (editDetails) {
            // 用游標判斷從哪裡開始修改，避免連續相同的字把樣式移到另一個字。
            const removed = editDetails.start === editDetails.end && editDetails.type.endsWith('Backward')
                ? Math.max(0, editDetails.value.length - textInput.value.length) : 0;
            const leading = editDetails.value.match(/^\p{White_Space}*/u)[0].length;
            untouchedEnd = Math.max(0, editDetails.start - removed - leading);
        }
        let prefix = 0;
        while (prefix < characters.length && prefix < next.length &&
            characters[prefix].index + characters[prefix].segment.length <= untouchedEnd &&
            characters[prefix].segment === next[prefix].segment) {
            next[prefix].style = characters[prefix].style;
            prefix++;
        }
        let previousIndex = characters.length - 1;
        let nextIndex = next.length - 1;
        while (previousIndex >= prefix && nextIndex >= prefix &&
            characters[previousIndex].segment === next[nextIndex].segment) {
            next[nextIndex].style = characters[previousIndex].style;
            previousIndex--;
            nextIndex--;
        }
        if (selectedIndex > previousIndex) selectedIndex += next.length - characters.length;
        selectedIndex = Math.max(0, Math.min(next.length - 1, selectedIndex));
        characters = next;
        text = nextText;
        renderButtons();
        writeStyles();
    });
}
