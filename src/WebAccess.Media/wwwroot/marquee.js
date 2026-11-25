function createMarqueeRenderer(ticker) {
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

    return function renderMarquee(settings) {
        ticker.hidden = !settings?.marquee;
        if (ticker.hidden) return;
        track.textContent = settings.marquee;
        ticker.classList.toggle('top', settings.marqueeAtTop);
        ticker.style.fontSize = settings.marqueeFontSize + 'px';
        speed = settings.marqueeSpeed;
        updateDuration();
    };
}
