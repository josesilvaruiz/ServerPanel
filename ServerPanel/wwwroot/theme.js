window.scrollConsoleToBottom = function (id) {
    const el = document.getElementById(id);
    if (el) el.scrollTop = el.scrollHeight;
};

window.themeManager = {
    apply: function (theme) {
        document.documentElement.setAttribute('data-theme', theme);
        localStorage.setItem('sp-theme', theme);
        document.cookie = 'sp-theme=' + theme + '; path=/; max-age=' + (365 * 24 * 60 * 60) + '; SameSite=Lax';
    },
    load: function () {
        const t = localStorage.getItem('sp-theme')
            || document.documentElement.getAttribute('data-theme')
            || 'default';
        document.documentElement.setAttribute('data-theme', t);
        return t;
    }
};

document.addEventListener('enhancedload', function () {
    const t = localStorage.getItem('sp-theme') || 'default';
    document.documentElement.setAttribute('data-theme', t);
    const l = localStorage.getItem('sp-layout') || 'sidebar';
    document.documentElement.setAttribute('data-layout', l);
    document.body.classList.remove('nav-open', 'tb-open');
});

window.navManager = {
    toggle:   function () { document.body.classList.toggle('nav-open'); },
    close:    function () { document.body.classList.remove('nav-open'); },
    tbToggle: function () { document.body.classList.toggle('tb-open'); },
    tbClose:  function () { document.body.classList.remove('tb-open'); }
};

window.layoutManager = {
    apply: function (layout) {
        document.documentElement.setAttribute('data-layout', layout);
        localStorage.setItem('sp-layout', layout);
        document.cookie = 'sp-layout=' + layout + '; path=/; max-age=' + (365 * 24 * 60 * 60) + '; SameSite=Lax';
    },
    load: function () {
        const l = localStorage.getItem('sp-layout')
            || document.documentElement.getAttribute('data-layout')
            || 'sidebar';
        document.documentElement.setAttribute('data-layout', l);
        return l;
    },
    toggle: function () {
        const current = document.documentElement.getAttribute('data-layout') || 'sidebar';
        const next = current === 'topbar' ? 'sidebar' : 'topbar';
        this.apply(next);
        return next;
    }
};
