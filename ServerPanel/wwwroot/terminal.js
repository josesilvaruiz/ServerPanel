// ── Terminal floating windows ───────────────────────────────────────────────

window.focusElement = function (id) {
    const el = document.getElementById(id);
    if (el) { el.focus(); }
};

window.scrollElementToBottom = function (id) {
    const el = document.getElementById(id);
    if (el) el.scrollTop = el.scrollHeight;
};

window.getWindowPos = function (id) {
    const el = document.getElementById(id);
    if (!el) return null;
    const s = el.style;
    return [parseFloat(s.left) || 0, parseFloat(s.top) || 0,
            parseFloat(s.width) || 700, parseFloat(s.height) || 460];
};

window.initDragWindow = function (winId, titlebarId, dotNet) {
    const win = document.getElementById(winId);
    const bar = document.getElementById(titlebarId);
    if (!win || !bar) return;

    let dragging = false, ox = 0, oy = 0;

    bar.addEventListener('mousedown', function (e) {
        if (e.button !== 0) return;
        if (e.target.closest('button,input,select')) return;
        dragging = true;
        ox = e.clientX - win.offsetLeft;
        oy = e.clientY - win.offsetTop;
        win.style.transition = 'none';
        e.preventDefault();
    });

    document.addEventListener('mousemove', function (e) {
        if (!dragging) return;
        win.style.left = (e.clientX - ox) + 'px';
        win.style.top  = (e.clientY - oy) + 'px';
    });

    document.addEventListener('mouseup', function () {
        if (!dragging) return;
        dragging = false;
        if (dotNet) {
            dotNet.invokeMethodAsync('OnWindowGeometry', winId,
                win.offsetLeft, win.offsetTop, win.offsetWidth, win.offsetHeight);
        }
    });
};

window.initResizeWindow = function (winId, handleId, dotNet) {
    const win    = document.getElementById(winId);
    const handle = document.getElementById(handleId);
    if (!win || !handle) return;

    let resizing = false, sx = 0, sy = 0, sw = 0, sh = 0;

    handle.addEventListener('mousedown', function (e) {
        if (e.button !== 0) return;
        resizing = true;
        sx = e.clientX; sy = e.clientY;
        sw = win.offsetWidth; sh = win.offsetHeight;
        e.preventDefault();
        e.stopPropagation();
    });

    document.addEventListener('mousemove', function (e) {
        if (!resizing) return;
        const w = Math.max(340, sw + e.clientX - sx);
        const h = Math.max(180, sh + e.clientY - sy);
        win.style.width  = w + 'px';
        win.style.height = h + 'px';
    });

    document.addEventListener('mouseup', function () {
        if (!resizing) return;
        resizing = false;
        if (dotNet) {
            dotNet.invokeMethodAsync('OnWindowGeometry', winId,
                win.offsetLeft, win.offsetTop, win.offsetWidth, win.offsetHeight);
        }
    });
};

// ── Sidebar resize ──────────────────────────────────────────────────────────

window.initSidebarResize = function (sidebarId, handleId) {
    const sidebar = document.getElementById(sidebarId);
    const handle  = document.getElementById(handleId);
    if (!sidebar || !handle) return;

    const MIN = 200, MAX = 520;
    let dragging = false, startX = 0, startW = 0;

    // Restore saved width
    const saved = parseInt(localStorage.getItem('term-sidebar-width'));
    if (saved && saved >= MIN && saved <= MAX) sidebar.style.width = saved + 'px';

    handle.addEventListener('mousedown', function (e) {
        if (e.button !== 0) return;
        dragging = true;
        startX = e.clientX;
        startW = sidebar.offsetWidth;
        document.body.style.cursor = 'col-resize';
        document.body.style.userSelect = 'none';
        e.preventDefault();
    });

    document.addEventListener('mousemove', function (e) {
        if (!dragging) return;
        const w = Math.min(MAX, Math.max(MIN, startW + e.clientX - startX));
        sidebar.style.width = w + 'px';
    });

    document.addEventListener('mouseup', function () {
        if (!dragging) return;
        dragging = false;
        document.body.style.cursor = '';
        document.body.style.userSelect = '';
        localStorage.setItem('term-sidebar-width', sidebar.offsetWidth);
    });
};
