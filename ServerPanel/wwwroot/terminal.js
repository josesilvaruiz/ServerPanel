// ── Terminal floating windows ───────────────────────────────────────────────

window.setWindowGeometry = function (id, left, top, width, height) {
    const el = document.getElementById(id);
    if (!el) return;
    el.style.left   = left   + 'px';
    el.style.top    = top    + 'px';
    el.style.width  = width  + 'px';
    el.style.height = height + 'px';
};

window.focusElement = function (id) {
    const el = document.getElementById(id);
    if (el) el.focus();
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
        if (e.target.closest('button,input,select,label')) return;
        dragging = true;
        // Use style.left/top (already set by Blazor) as origin — avoids offsetParent issues
        ox = e.clientX - (parseFloat(win.style.left) || 0);
        oy = e.clientY - (parseFloat(win.style.top)  || 0);
        win.style.transition = 'none';
        document.body.style.userSelect = 'none';
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
        document.body.style.userSelect = '';
        if (dotNet) dotNet.invokeMethodAsync('OnWindowGeometry', winId,
            parseFloat(win.style.left) || 0, parseFloat(win.style.top) || 0,
            parseFloat(win.style.width) || 700, parseFloat(win.style.height) || 460);
    });
};

window.initResizeWindow = function (winId, handleId, dotNet, dir) {
    dir = dir || 'se';
    const win    = document.getElementById(winId);
    const handle = document.getElementById(handleId);
    if (!win || !handle) return;

    let resizing = false, sx = 0, sy = 0, sw = 0, sh = 0, sl = 0, st = 0;

    handle.addEventListener('mousedown', function (e) {
        if (e.button !== 0) return;
        resizing = true;
        sx = e.clientX; sy = e.clientY;
        sw = parseFloat(win.style.width)  || win.offsetWidth;
        sh = parseFloat(win.style.height) || win.offsetHeight;
        sl = parseFloat(win.style.left) || 0;
        st = parseFloat(win.style.top)  || 0;
        document.body.style.cursor = dir + '-resize';
        document.body.style.userSelect = 'none';
        e.preventDefault();
        e.stopPropagation();
    });

    document.addEventListener('mousemove', function (e) {
        if (!resizing) return;
        const dx = e.clientX - sx;
        const dy = e.clientY - sy;
        let newW = sw, newH = sh, newL = sl, newT = st;
        if (dir.includes('e')) newW = Math.max(340, sw + dx);
        if (dir.includes('s')) newH = Math.max(180, sh + dy);
        if (dir.includes('w')) { newW = Math.max(340, sw - dx); newL = sl + (sw - newW); }
        if (dir.includes('n')) { newH = Math.max(180, sh - dy); newT = st + (sh - newH); }
        win.style.width  = newW + 'px';
        win.style.height = newH + 'px';
        win.style.left   = newL + 'px';
        win.style.top    = newT + 'px';
    });

    document.addEventListener('mouseup', function () {
        if (!resizing) return;
        resizing = false;
        document.body.style.cursor = '';
        document.body.style.userSelect = '';
        if (dotNet) dotNet.invokeMethodAsync('OnWindowGeometry', winId,
            parseFloat(win.style.left) || 0, parseFloat(win.style.top) || 0,
            parseFloat(win.style.width) || 700, parseFloat(win.style.height) || 460);
    });
};

// ── Sidebar resize ──────────────────────────────────────────────────────────

window.initSidebarResize = function (sidebarId, handleId) {
    const sidebar = document.getElementById(sidebarId);
    const handle  = document.getElementById(handleId);
    if (!sidebar || !handle) return;

    const MIN = 200, MAX = 520;
    let dragging = false, startX = 0, startW = 0;

    const saved = parseInt(localStorage.getItem('term-sidebar-width'));
    if (saved >= MIN && saved <= MAX) sidebar.style.width = saved + 'px';

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
