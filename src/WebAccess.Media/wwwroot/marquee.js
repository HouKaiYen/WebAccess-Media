function applyMarqueeAppearance(element, settings) {
    element.style.fontSize = settings.marqueeFontSize + 'px';
    element.style.fontWeight = settings.marqueeFontWeight || '';
    element.style.color = settings.marqueeTextColor || '';
    const alpha = Math.round((settings.marqueeBackgroundOpacity ?? 91) * 255 / 100).toString(16).padStart(2, '0');
    element.style.backgroundColor = settings.marqueeBackgroundColor ? settings.marqueeBackgroundColor + alpha : '';
}

function renderMarqueeText(element, text, styles) {
    if (!styles?.length) {
        element.textContent = text;
        return;
    }
    const content = document.createDocumentFragment();
    let position = 0;
    for (const style of styles) {
        const end = style.start + style.length;
        if (style.start < position || style.length < 1 || end > text.length) continue;
        content.append(text.slice(position, style.start));
        const character = document.createElement('span');
        character.textContent = text.slice(style.start, end);
        if (style.textColor) character.style.color = style.textColor;
        if (style.fontSize) character.style.fontSize = style.fontSize + 'px';
        if (style.fontWeight) character.style.fontWeight = style.fontWeight;
        content.append(character);
        position = end;
    }
    content.append(text.slice(position));
    element.replaceChildren(content);
}

function createMarqueeRenderer(ticker, layout) {
    const track = ticker.firstElementChild;
    let speed;
    function updateDuration() {
        if (ticker.hidden || !(speed > 0)) return;
        const distance = track.offsetWidth;
        if (distance > 0) {
            track.style.setProperty('--marquee-duration', distance / speed + 's');
        }
    }
    new ResizeObserver(updateDuration).observe(track, { box: 'border-box' });
    function updateLayout() {
        const height = ticker.offsetHeight;
        const atTop = ticker.classList.contains('top');
        layout.style.setProperty('--marquee-top-height', (atTop ? height : 0) + 'px');
        layout.style.setProperty('--marquee-bottom-height', (atTop ? 0 : height) + 'px');
    }
    new ResizeObserver(updateLayout).observe(ticker);

    return function renderMarquee(settings) {
        ticker.hidden = !settings?.marquee || settings.marqueeEnabled === false;
        if (!ticker.hidden) {
            renderMarqueeText(track, settings.marquee, settings.marqueeCharacterStyles);
            ticker.classList.toggle('top', settings.marqueeAtTop);
            applyMarqueeAppearance(ticker, settings);
            speed = settings.marqueeSpeed;
            updateDuration();
        }
        updateLayout();
    };
}
