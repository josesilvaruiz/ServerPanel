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
        const ws = win.offsetParent || win.parentElement;
        const maxL = ws ? Math.max(0, ws.offsetWidth  - win.offsetWidth)  : 9999;
        const maxT = ws ? Math.max(0, ws.offsetHeight - win.offsetHeight) : 9999;
        win.style.left = Math.min(maxL, Math.max(0, e.clientX - ox)) + 'px';
        win.style.top  = Math.min(maxT, Math.max(0, e.clientY - oy)) + 'px';
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
        const ws = win.offsetParent || win.parentElement;
        const wsW = ws ? ws.offsetWidth  : 9999;
        const wsH = ws ? ws.offsetHeight : 9999;
        const dx = e.clientX - sx;
        const dy = e.clientY - sy;
        let newW = sw, newH = sh, newL = sl, newT = st;
        if (dir.includes('e')) newW = Math.min(wsW - newL, Math.max(340, sw + dx));
        if (dir.includes('s')) newH = Math.min(wsH - newT, Math.max(180, sh + dy));
        if (dir.includes('w')) { newW = Math.max(340, sw - dx); newL = Math.max(0, sl + (sw - newW)); newW = Math.min(newW, sl + sw); }
        if (dir.includes('n')) { newH = Math.max(180, sh - dy); newT = Math.max(0, st + (sh - newH)); newH = Math.min(newH, st + sh); }
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

// ── Cmd panel — dockable / floating ─────────────────────────────────────────

let _cmdSnapEl = null;

function _cmdShowSnap(container, dock, pw, ph) {
    if (!_cmdSnapEl) {
        _cmdSnapEl = document.createElement('div');
        _cmdSnapEl.style.cssText = 'position:absolute;background:rgba(74,222,128,.13);border:2px solid rgba(74,222,128,.45);border-radius:7px;pointer-events:none;z-index:9100;transition:all .1s;display:none';
        container.appendChild(_cmdSnapEl);
    }
    if (!dock) { _cmdSnapEl.style.display = 'none'; return; }
    const s = _cmdSnapEl.style;
    s.display = 'block';
    s.left = s.top = s.right = s.bottom = s.width = s.height = '';
    const cw = container.offsetWidth, ch = container.offsetHeight;
    if (dock === 'left')   { s.left='0';s.top='0';s.width=pw+'px';s.height=ch+'px'; }
    if (dock === 'right')  { s.right='0';s.top='0';s.width=pw+'px';s.height=ch+'px'; }
    if (dock === 'top')    { s.left='0';s.top='0';s.width=cw+'px';s.height=ph+'px'; }
    if (dock === 'bottom') { s.left='0';s.bottom='0';s.width=cw+'px';s.height=ph+'px'; }
}

window.initCmdPanel = function (panelId, barId, dotNet) {
    const panel = document.getElementById(panelId);
    const bar   = document.getElementById(barId);
    if (!panel || !bar || bar._cmdInited) return;
    bar._cmdInited = true;

    const SNAP = 72;
    let drag = false, ox = 0, oy = 0;

    bar.addEventListener('mousedown', function (e) {
        if (e.button !== 0) return;
        if (e.target.closest('button,input,select,label')) return;
        drag = true;
        // Convert any % or right/bottom anchors to px before dragging
        if (panel.style.height.endsWith('%')) panel.style.height = panel.offsetHeight + 'px';
        if (panel.style.width.endsWith('%'))  panel.style.width  = panel.offsetWidth  + 'px';
        if (!panel.style.left || panel.style.left === '') {
            panel.style.left  = panel.offsetLeft + 'px';
            panel.style.right = '';
        }
        if (!panel.style.top || panel.style.top === '') {
            panel.style.top    = panel.offsetTop + 'px';
            panel.style.bottom = '';
        }
        ox = e.clientX - (parseFloat(panel.style.left) || 0);
        oy = e.clientY - (parseFloat(panel.style.top)  || 0);
        panel.style.transition = 'none';
        document.body.style.userSelect = 'none';
        e.preventDefault();
    });

    document.addEventListener('mousemove', function (e) {
        if (!drag) return;
        const c  = panel.offsetParent;
        const cw = c.offsetWidth, ch = c.offsetHeight;
        const pw = panel.offsetWidth, ph = panel.offsetHeight;
        const x = e.clientX - ox, y = e.clientY - oy;
        panel.style.left   = x + 'px';
        panel.style.top    = y + 'px';
        panel.style.right  = '';
        panel.style.bottom = '';
        const dock = x <= SNAP ? 'left'
                   : x + pw >= cw - SNAP ? 'right'
                   : y <= SNAP ? 'top'
                   : y + ph >= ch - SNAP ? 'bottom'
                   : null;
        _cmdShowSnap(c, dock, pw, ph);
    });

    document.addEventListener('mouseup', function () {
        if (!drag) return;
        drag = false;
        document.body.style.userSelect = '';
        _cmdShowSnap(panel.offsetParent, null, 0, 0);
        const c  = panel.offsetParent;
        const cw = c.offsetWidth, ch = c.offsetHeight;
        const pw = panel.offsetWidth, ph = panel.offsetHeight;
        const x  = parseFloat(panel.style.left) || 0;
        const y  = parseFloat(panel.style.top)  || 0;
        const dock = x <= SNAP ? 'left'
                   : x + pw >= cw - SNAP ? 'right'
                   : y <= SNAP ? 'top'
                   : y + ph >= ch - SNAP ? 'bottom'
                   : 'float';
        dotNet.invokeMethodAsync('OnCmdPanelDocked', dock, x, y, pw, ph);
    });
};

window.initCmdPanelResize = function (panelId, handleId, dotNet, dir) {
    const panel  = document.getElementById(panelId);
    const handle = document.getElementById(handleId);
    if (!panel || !handle || handle._cmdResInited) return;
    handle._cmdResInited = true;

    let res = false, sx = 0, sy = 0, sw = 0, sh = 0, sl = 0, st = 0;

    handle.addEventListener('mousedown', function (e) {
        if (e.button !== 0) return;
        res = true;
        sx = e.clientX; sy = e.clientY;
        sw = panel.offsetWidth;  sh = panel.offsetHeight;
        sl = parseFloat(panel.style.left) || panel.offsetLeft;
        st = parseFloat(panel.style.top)  || panel.offsetTop;
        document.body.style.cursor = dir + '-resize';
        document.body.style.userSelect = 'none';
        e.preventDefault(); e.stopPropagation();
    });

    document.addEventListener('mousemove', function (e) {
        if (!res) return;
        const c  = panel.offsetParent;
        const cw = c.offsetWidth, ch = c.offsetHeight;
        const dx = e.clientX - sx, dy = e.clientY - sy;
        let nw = sw, nh = sh, nl = sl, nt = st;
        if (dir.includes('e')) nw = Math.min(cw - nl, Math.max(220, sw + dx));
        if (dir.includes('s')) nh = Math.min(ch - nt, Math.max(160, sh + dy));
        if (dir.includes('w')) { nw = Math.max(220, sw - dx); nl = Math.max(0, sl + sw - nw); }
        if (dir.includes('n')) { nh = Math.max(160, sh - dy); nt = Math.max(0, st + sh - nh); }
        panel.style.width  = nw + 'px';
        panel.style.height = nh + 'px';
        panel.style.left   = nl + 'px';
        panel.style.top    = nt + 'px';
        panel.style.right  = '';
        panel.style.bottom = '';
    });

    document.addEventListener('mouseup', function () {
        if (!res) return;
        res = false;
        document.body.style.cursor = '';
        document.body.style.userSelect = '';
        dotNet.invokeMethodAsync('OnCmdPanelGeometry',
            parseFloat(panel.style.left) || 0,
            parseFloat(panel.style.top)  || 0,
            panel.offsetWidth, panel.offsetHeight);
    });
};

window.downloadTextFile = function (filename, content) {
    const a = document.createElement('a');
    a.href = 'data:text/plain;charset=utf-8,' + encodeURIComponent(content);
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
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
