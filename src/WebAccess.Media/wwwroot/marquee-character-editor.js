function normalizeMarqueeText(value) {
    return value.replace(/\r\n?/g, '\n').replace(/^\p{White_Space}+|\p{White_Space}+$/gu, '');
}

function setupMarqueeCharacterEditor(textInput, readSettings, markDirty) {
    const editorPanel = document.getElementById('marquee-editor');
    const editor = document.getElementById('marquee-rich-text');
    const hiddenStyles = document.getElementById('MarqueeCharacterStyles');
    if (typeof Intl.Segmenter !== 'function' || !editor.isContentEditable) {
        const original = normalizeMarqueeText(textInput.value);
        const originalStyles = hiddenStyles.value;
        textInput.addEventListener('input', () => {
            hiddenStyles.value = normalizeMarqueeText(textInput.value) === original ? originalStyles : '[]';
        });
        return;
    }

    const tools = document.getElementById('marquee-range-tools');
    const status = document.getElementById('marquee-editor-status');
    const color = document.getElementById('marquee-range-color');
    const colorStatus = document.getElementById('marquee-range-color-status');
    const size = document.getElementById('marquee-range-size');
    const weight = document.getElementById('marquee-range-weight');
    const reset = document.getElementById('reset-marquee-range');
    const inheritStyle = document.getElementById('inherit-marquee-range');
    const defaultStyle = {
        textColor: reset.dataset.defaultColor,
        fontSize: Number(reset.dataset.defaultSize),
        fontWeight: Number(reset.dataset.defaultWeight)
    };
    const segmenter = new Intl.Segmenter('zh-Hant', { granularity: 'grapheme' });
    const history = [];
    const redo = [];
    let text = textInput.value;
    let characters = splitCharacters(text, readSettings().marqueeCharacterStyles);
    let selection = { start: 0, end: 0 };
    let edit = null;
    let composing = false;

    function splitCharacters(value, styles = []) {
        const overrides = new Map((styles ?? []).map(style => [style.start, style]));
        return Array.from(segmenter.segment(value), ({ segment, index }) => {
            const saved = overrides.get(index);
            const style = {};
            if (saved?.length === segment.length) {
                for (const property of ['textColor', 'fontSize', 'fontWeight']) {
                    if (saved[property] != null) style[property] = saved[property];
                }
            }
            return { segment, index, style };
        });
    }

    function collectStyles(start = 0, end = text.length) {
        return characters.filter(character =>
            character.index >= start && character.index + character.segment.length <= end && Object.keys(character.style).length
        ).map(character => ({ start: character.index - start, length: character.segment.length, ...character.style }));
    }

    function snapshot(range = selection) {
        return { text, styles: collectStyles(), selection: range };
    }

    function remember(range) {
        history.push(snapshot(range));
        if (history.length > 50) history.shift();
        redo.length = 0;
    }

    function selectionInsideEditor() {
        const current = window.getSelection();
        if (!current?.rangeCount) return null;
        const range = current.getRangeAt(0);
        if (!editor.contains(range.startContainer) || !editor.contains(range.endContainer)) return null;
        const prefix = document.createRange();
        prefix.selectNodeContents(editor);
        prefix.setEnd(range.startContainer, range.startOffset);
        const start = prefix.toString().length;
        prefix.setEnd(range.endContainer, range.endOffset);
        return { start, end: prefix.toString().length };
    }

    function rangeForSelection() {
        const walker = document.createTreeWalker(editor, NodeFilter.SHOW_TEXT);
        const range = document.createRange();
        range.selectNodeContents(editor);
        range.collapse(false);
        let offset = 0;
        let hasStart = false;
        let node = walker.nextNode();
        while (node) {
            const end = offset + node.length;
            if (!hasStart && selection.start <= end) {
                range.setStart(node, selection.start - offset);
                hasStart = true;
            }
            if (selection.end <= end) {
                range.setEnd(node, selection.end - offset);
                break;
            }
            offset = end;
            node = walker.nextNode();
        }
        return range;
    }

    function selectedCharacters() {
        if (selection.start === selection.end) return [];
        return characters.filter(character =>
            character.index < selection.end && character.index + character.segment.length > selection.start);
    }

    function uniformStyle(selected, property) {
        const first = selected[0]?.style[property];
        return selected.every(character => character.style[property] === first) ? first : null;
    }

    function updateToolbar() {
        const selected = selectedCharacters();
        tools.disabled = selected.length === 0;
        const message = selected.length ? '已選取 ' + selected.length + ' 個字元' : '反白選取要調整的文字。';
        if (status.textContent !== message) status.textContent = message;
        const selectedColor = uniformStyle(selected, 'textColor');
        colorStatus.textContent = !selected.length ? '' : selectedColor === null ? '混合' : selectedColor ?? '沿用全文';
        if (selected.length) {
            const selectedSize = uniformStyle(selected, 'fontSize');
            const selectedWeight = uniformStyle(selected, 'fontWeight');
            color.value = selectedColor ?? document.getElementById('MarqueeTextColor').value;
            if (document.activeElement !== size) size.value = selectedSize ?? '';
            size.placeholder = selectedSize === null ? '混合' : '沿用全文';
            weight.value = selectedWeight === null ? 'mixed' : selectedWeight ?? '';
            inheritStyle.disabled = !selected.some(({ style }) => Object.keys(style).length);
            reset.disabled = selected.every(({ style }) => style.textColor === defaultStyle.textColor &&
                style.fontSize === defaultStyle.fontSize && style.fontWeight === defaultStyle.fontWeight);
        }
        const highlights = globalThis.CSS?.highlights;
        if (highlights && typeof Highlight === 'function') {
            if (selected.length && document.activeElement !== editor) {
                highlights.set('marquee-selection', new Highlight(rangeForSelection()));
            } else {
                highlights.delete('marquee-selection');
            }
        }
    }

    function render(restoreCaret = document.activeElement === editor) {
        renderMarqueeText(editor, text, collectStyles());
        if (restoreCaret) {
            const current = window.getSelection();
            current.removeAllRanges();
            current.addRange(rangeForSelection());
        }
        updateToolbar();
    }

    function writeDraft() {
        const start = text.match(/^\p{White_Space}*/u)[0].length;
        const end = start + normalizeMarqueeText(text).length;
        textInput.value = text;
        hiddenStyles.value = JSON.stringify(collectStyles(start, end));
        markDirty();
    }

    function updateText(nextText, caret, previousRange, inputType) {
        edit = null;
        if (nextText.length > textInput.maxLength) {
            selection = previousRange;
            render();
            status.textContent = '公告文字上限為 500 字元。';
            return;
        }
        selection = { start: Math.min(caret.start, nextText.length), end: Math.min(caret.end, nextText.length) };
        if (nextText === text) {
            render();
            return;
        }

        remember(previousRange);
        const next = splitCharacters(nextText);
        // 用編輯前的游標區分相鄰的相同字，讓保留的字沿用原有格式。
        const deletedBeforeCaret = inputType?.endsWith('Backward') && previousRange.start === previousRange.end
            ? Math.max(0, text.length - nextText.length) : 0;
        const untouchedEnd = previousRange.start - deletedBeforeCaret;
        let prefix = 0;
        while (prefix < characters.length && prefix < next.length &&
            characters[prefix].index + characters[prefix].segment.length <= untouchedEnd &&
            characters[prefix].segment === next[prefix].segment) {
            next[prefix].style = characters[prefix].style;
            prefix++;
        }
        let oldIndex = characters.length - 1;
        let newIndex = next.length - 1;
        while (oldIndex >= prefix && newIndex >= prefix && characters[oldIndex].segment === next[newIndex].segment) {
            next[newIndex].style = characters[oldIndex].style;
            oldIndex--;
            newIndex--;
        }
        characters = next;
        text = nextText;
        writeDraft();
        render();
    }

    function synchronizeInput() {
        const next = editor.querySelector('br, div, p') ? editor.innerText : editor.textContent;
        const previousRange = edit?.selection ?? selection;
        updateText(next, selectionInsideEditor() ?? previousRange, previousRange, edit?.type);
    }

    function insertText(value) {
        const range = selectionInsideEditor() ?? selection;
        const available = textInput.maxLength - (text.length - (range.end - range.start));
        const normalized = value.replace(/\r\n?/g, '\n');
        const inserted = Array.from(segmenter.segment(normalized))
            .filter(({ segment, index }) => index + segment.length <= available)
            .map(({ segment }) => segment).join('');
        const caret = range.start + inserted.length;
        updateText(text.slice(0, range.start) + inserted + text.slice(range.end), { start: caret, end: caret }, range, 'insertText');
    }

    function restoreHistory(source, destination) {
        const previous = source.pop();
        if (!previous) return;
        destination.push(snapshot());
        text = previous.text;
        characters = splitCharacters(text, previous.styles);
        selection = previous.selection;
        writeDraft();
        render(true);
    }

    function formatSelected(update) {
        const selected = selectedCharacters();
        if (!selected.length) return;
        remember();
        for (const character of selected) update(character);
        writeDraft();
        render();
    }

    function setFormat(property, value) {
        formatSelected(character => {
            if (value === null) delete character.style[property];
            else character.style[property] = value;
        });
    }

    function captureSelection() {
        const range = selectionInsideEditor();
        if (!range) return;
        selection = range;
        updateToolbar();
    }

    function releaseSelection(event) {
        if (editorPanel.contains(event.target) || selection.start === selection.end) return;
        selection = { start: selection.end, end: selection.end };
        if (selectionInsideEditor()) window.getSelection().removeAllRanges();
        updateToolbar();
    }

    textInput.hidden = true;
    editorPanel.hidden = false;
    applyMarqueeAppearance(editor, readSettings());
    render(false);
    document.getElementById('marquee-text-label').addEventListener('click', event => {
        event.preventDefault();
        editor.focus();
    });

    document.addEventListener('selectionchange', () => {
        if (!composing && document.activeElement === editor) captureSelection();
    });
    document.addEventListener('pointerdown', releaseSelection);
    document.addEventListener('focusin', releaseSelection);
    editor.addEventListener('blur', updateToolbar);
    tools.addEventListener('pointerdown', () => {
        if (document.activeElement === editor) captureSelection();
    });
    color.addEventListener('input', () => setFormat('textColor', color.value));
    size.addEventListener('input', () => {
        if (size.validity.valid) setFormat('fontSize', size.value ? size.valueAsNumber : null);
    });
    weight.addEventListener('change', () => setFormat('fontWeight', weight.value ? Number(weight.value) : null));
    inheritStyle.addEventListener('click', () => {
        formatSelected(character => { character.style = {}; });
        color.focus();
    });
    reset.addEventListener('click', () => {
        formatSelected(character => { character.style = { ...defaultStyle }; });
        color.focus();
    });

    editor.addEventListener('beforeinput', event => {
        if (composing || event.isComposing) return;
        const type = event.inputType;
        if (type === 'insertParagraph' || type === 'insertLineBreak') {
            event.preventDefault();
            insertText('\n');
        } else if (type === 'historyUndo' || type === 'historyRedo') {
            event.preventDefault();
            if (type === 'historyUndo') restoreHistory(history, redo);
            else restoreHistory(redo, history);
        } else {
            edit = { selection: selectionInsideEditor() ?? selection, type };
        }
    });
    editor.addEventListener('input', event => {
        if (!composing && !event.isComposing) synchronizeInput();
    });
    editor.addEventListener('paste', event => {
        event.preventDefault();
        insertText(event.clipboardData.getData('text/plain'));
    });
    editor.addEventListener('compositionstart', () => {
        composing = true;
        edit = { selection: selectionInsideEditor() ?? selection, type: 'insertCompositionText' };
    });
    editor.addEventListener('compositionend', () => {
        composing = false;
        synchronizeInput();
    });
    editor.addEventListener('keydown', event => {
        if (composing || !(event.ctrlKey || event.metaKey) || event.altKey) return;
        const key = event.key.toLowerCase();
        if (key !== 'z' && key !== 'y') return;
        event.preventDefault();
        if (key === 'y' || event.shiftKey) restoreHistory(redo, history);
        else restoreHistory(history, redo);
    });

    textInput.form.addEventListener('input', event => {
        if (!['MarqueeTextColor', 'MarqueeBackgroundColor', 'MarqueeBackgroundOpacity', 'MarqueeFontWeight', 'MarqueeFontSize'].includes(event.target.id)) return;
        applyMarqueeAppearance(editor, readSettings());
        updateToolbar();
    });
}
