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
        const sid = winId.startsWith('win-') ? winId.slice(4) : winId;
        window.xtermFit && window.xtermFit(sid);
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

// ── xterm.js + SignalR terminal ──────────────────────────────────────────────

let _termConn = null;
let _termConnPromise = null; // serializes concurrent start attempts
const _xtermInstances = {};

async function _getConnection() {
    if (_termConn && _termConn.state === signalR.HubConnectionState.Connected)
        return _termConn;

    // If a start is already in flight, wait for it
    if (_termConnPromise) return _termConnPromise;

    _termConnPromise = (async () => {
        const conn = new signalR.HubConnectionBuilder()
            .withUrl('/panel/hubs/terminal')
            .withAutomaticReconnect()
            .build();

        conn.on('Output', (sessionId, b64) => {
            const inst = _xtermInstances[sessionId];
            if (!inst) return;
            const bytes = Uint8Array.from(atob(b64), c => c.charCodeAt(0));
            inst.term.write(bytes);
        });

        conn.on('Closed', (sessionId) => {
            const inst = _xtermInstances[sessionId];
            if (inst) inst.term.writeln('\r\n\x1b[31m[conexión cerrada]\x1b[0m');
        });

        conn.on('Error', (sessionId, msg) => {
            const inst = _xtermInstances[sessionId];
            if (inst) inst.term.writeln('\r\n\x1b[31mError SSH: ' + msg + '\x1b[0m');
        });

        conn.onreconnected(() => {
            for (const [sid, inst] of Object.entries(_xtermInstances)) {
                inst.term.writeln('\r\n\x1b[33m[reconectando...]\x1b[0m');
                conn.invoke('OpenTerminal', sid, inst.term.cols, inst.term.rows).catch(() => {});
            }
        });

        await conn.start();

        if (conn.state !== signalR.HubConnectionState.Connected)
            throw new Error('SignalR no alcanzó estado Connected tras start()');

        _termConn = conn;
        _termConnPromise = null;
        return conn;
    })();

    return _termConnPromise;
}

function _toBase64(str) {
    const bytes = new TextEncoder().encode(str);
    let bin = '';
    for (let i = 0; i < bytes.length; i++) bin += String.fromCharCode(bytes[i]);
    return btoa(bin);
}

window.initXterm = async function (sessionId, fontSize) {
    if (_xtermInstances[sessionId]) return true;

    const container = document.getElementById('xterm-' + sessionId);
    if (!container) return false;

    // ── 1. Create Terminal ───────────────────────────────────
    let term;
    try {
        term = new Terminal({
            cursorBlink: true,
            fontFamily: '"Cascadia Code","Fira Code","Consolas",monospace',
            fontSize: fontSize || 15,
            cols: 80, rows: 24,        // sensible defaults if FitAddon fails
            scrollback: 10000,
            theme: {
                background:'#0f1117', foreground:'#e2e8f0', cursor:'#4ade80',
                black:'#1e293b', brightBlack:'#475569',
                red:'#f87171',   brightRed:'#fca5a5',
                green:'#4ade80', brightGreen:'#86efac',
                yellow:'#fbbf24',brightYellow:'#fde68a',
                blue:'#60a5fa',  brightBlue:'#93c5fd',
                magenta:'#c084fc',brightMagenta:'#d8b4fe',
                cyan:'#22d3ee',  brightCyan:'#67e8f9',
                white:'#e2e8f0', brightWhite:'#f8fafc',
            },
            copyOnSelect: true,
            allowProposedApi: true,
        });
    } catch(e) {
        console.error('[xterm] Terminal constructor failed:', e);
        container.innerHTML = '<pre style="color:#f87171;padding:8px">Terminal no disponible: ' + e.message + '</pre>';
        return false;
    }

    // ── 2. Load FitAddon (optional — handle both global name styles) ──
    let fitAddon = null;
    try {
        // @xterm/addon-fit may export as FitAddon.FitAddon (UMD namespace) or just FitAddon
        const FA = window.FitAddon;
        const FitAddonCls = FA && typeof FA.FitAddon === 'function' ? FA.FitAddon
                          : FA && typeof FA === 'function'          ? FA
                          : null;
        if (FitAddonCls) {
            fitAddon = new FitAddonCls();
            term.loadAddon(fitAddon);
        } else {
            console.warn('[xterm] FitAddon not found — using fixed 80x24');
        }
    } catch(e) {
        console.warn('[xterm] FitAddon failed:', e);
        fitAddon = null;
    }

    // ── 3. Open terminal ─────────────────────────────────────
    try {
        term.open(container);
    } catch(e) {
        console.error('[xterm] term.open() failed:', e);
        return false;
    }

    // ── 4. Guard + handlers BEFORE any await to prevent double-init race ───────
    // Blazor can fire OnAfterRenderAsync again during the rAF below; setting the
    // instance here ensures the guard check in initXterm returns early on the
    // second call instead of creating a second terminal that overwrites the DOM.
    _xtermInstances[sessionId] = { term, fitAddon };

    // Send keystrokes — no state check, just try; invoke fails gracefully if disconnected
    term.onData(data => {
        if (_termConn)
            _termConn.invoke('Input', sessionId, _toBase64(data)).catch(() => {});
    });

    // Focus xterm whenever the user clicks anywhere inside the terminal window
    // (covers title bar, toolbar, and the xterm area itself)
    const windowEl = document.getElementById('win-' + sessionId);
    if (windowEl) {
        windowEl.addEventListener('mousedown', (e) => {
            if (!e.target.closest('button, input, select, textarea, a'))
                setTimeout(() => term.focus(), 0);
        });
    }
    container.addEventListener('mousedown', () => term.focus());

    container.addEventListener('contextmenu', async (e) => {
        e.preventDefault();
        try {
            const text = await navigator.clipboard.readText();
            if (text) { term.paste(text); term.focus(); }
        } catch {}
    });

    // ── Fit + focus (needs layout to be flushed first) ───────────────────────
    await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));
    if (fitAddon) { try { fitAddon.fit(); } catch(e) {} }
    term.focus();

    // ── 5. SignalR + SSH ─────────────────────────────────────
    try {
        const conn = await _getConnection();
        await conn.invoke('OpenTerminal', sessionId, term.cols, term.rows);

        term.onTitleChange(title => {
            const el = document.getElementById('win-title-' + sessionId);
            if (el) el.textContent = title;
        });

        if (fitAddon) {
            const ro = new ResizeObserver(() => {
                if (container.offsetParent === null) return;
                try { fitAddon.fit(); } catch(e) {}
                conn.invoke('Resize', sessionId, term.cols, term.rows).catch(() => {});
            });
            ro.observe(container);
            _xtermInstances[sessionId].ro = ro;
        }
    } catch (err) {
        console.error('[xterm] SignalR/SSH error:', err);
        term.writeln('\r\n\x1b[31mError de conexión: ' + err.message + '\x1b[0m');
    }

    return true;
};

window.disposeXterm = function (sessionId) {
    const inst = _xtermInstances[sessionId];
    if (!inst) return;
    try { inst.ro && inst.ro.disconnect(); } catch {}
    try { inst.term.dispose(); } catch {}
    delete _xtermInstances[sessionId];
    if (_termConn && _termConn.state === signalR.HubConnectionState.Connected)
        _termConn.invoke('CloseTerminal', sessionId).catch(() => {});
};

window.xtermFocus = function (sessionId) {
    const inst = _xtermInstances[sessionId];
    if (inst) inst.term.focus();
};

window.xtermPaste = function (sessionId, text) {
    const inst = _xtermInstances[sessionId];
    if (!inst) return;
    inst.term.paste(text);
    inst.term.focus();
};

window.xtermSendLine = function (sessionId, text) {
    if (!_termConn || _termConn.state !== signalR.HubConnectionState.Connected) return;
    _termConn.invoke('Input', sessionId, _toBase64(text + '\n')).catch(() => {});
};

window.xtermClear = function (sessionId) {
    const inst = _xtermInstances[sessionId];
    if (inst) inst.term.clear();
};

window.xtermCopyAll = function (sessionId) {
    const inst = _xtermInstances[sessionId];
    if (!inst) return;
    const buffer = inst.term.buffer.active;
    const lines = [];
    for (let i = 0; i < buffer.length; i++) {
        const line = buffer.getLine(i);
        if (line) lines.push(line.translateToString(true));
    }
    while (lines.length > 0 && lines[lines.length - 1].trim() === '') lines.pop();
    const text = lines.join('\n');
    if (navigator.clipboard && text) navigator.clipboard.writeText(text).catch(() => {});
};

window.xtermPasteFromClipboard = async function (sessionId) {
    const inst = _xtermInstances[sessionId];
    if (!inst) return false;
    try {
        const text = await navigator.clipboard.readText();
        if (text) { inst.term.paste(text); inst.term.focus(); return true; }
    } catch(e) { console.warn('[xterm] clipboard paste failed:', e); }
    return false;
};

window.setXtermFontSize = function (sessionId, size) {
    const inst = _xtermInstances[sessionId];
    if (!inst) return;
    inst.term.options.fontSize = size;
    inst.fitAddon.fit();
};

window.xtermFit = function (sessionId) {
    const inst = _xtermInstances[sessionId];
    if (!inst) return;
    inst.fitAddon.fit();
    if (_termConn && _termConn.state === signalR.HubConnectionState.Connected)
        _termConn.invoke('Resize', sessionId, inst.term.cols, inst.term.rows).catch(() => {});
};

window.disposeAllXterms = function () {
    for (const sid of Object.keys(_xtermInstances)) window.disposeXterm(sid);
    if (_termConn) { _termConn.stop().catch(() => {}); _termConn = null; }
};

window.setZIndexAndFocus = function (winId, zIndex, sessionId) {
    const el = document.getElementById(winId);
    if (el) el.style.zIndex = zIndex;
    const inst = _xtermInstances[sessionId];
    if (inst) inst.term.focus();
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
