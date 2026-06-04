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

// Re-aplicar tema en cada navegación de Blazor (enhanced navigation)
document.addEventListener('enhancedload', function () {
    const t = localStorage.getItem('sp-theme') || 'default';
    document.documentElement.setAttribute('data-theme', t);
});
