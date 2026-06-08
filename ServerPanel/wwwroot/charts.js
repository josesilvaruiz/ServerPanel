window._spCharts = {};

function _spDestroyChart(id) {
    if (window._spCharts[id]) {
        window._spCharts[id].destroy();
        delete window._spCharts[id];
    }
}

function _spScales(ySuffix) {
    return {
        x: {
            ticks: { color: '#64748b', font: { size: 10 }, maxTicksLimit: 8, maxRotation: 0 },
            grid: { color: '#1e293b' }
        },
        y: {
            ticks: { color: '#64748b', font: { size: 10 }, callback: v => v + ySuffix },
            grid: { color: '#1e293b' },
            beginAtZero: true
        }
    };
}

window.spRenderChart = function (id, labels, values, label, color, ySuffix) {
    const canvas = document.getElementById(id);
    if (!canvas) return;
    _spDestroyChart(id);

    window._spCharts[id] = new Chart(canvas.getContext('2d'), {
        type: 'line',
        data: {
            labels,
            datasets: [{
                label,
                data: values,
                borderColor: color,
                backgroundColor: color + '18',
                borderWidth: 1.5,
                pointRadius: values.length > 60 ? 0 : 2,
                pointHoverRadius: 4,
                fill: true,
                tension: 0.3
            }]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            animation: false,
            plugins: {
                legend: { display: false },
                tooltip: { callbacks: { label: ctx => `${ctx.parsed.y}${ySuffix}` } }
            },
            scales: _spScales(ySuffix)
        }
    });
};

// Chart with per-point player name tooltips.
// playerNames: string[][] — one string[] per data point (empty array = no one).
window.spRenderChartWithPlayers = function (id, labels, values, label, color, playerNames) {
    const canvas = document.getElementById(id);
    if (!canvas) return;
    _spDestroyChart(id);

    window._spCharts[id] = new Chart(canvas.getContext('2d'), {
        type: 'line',
        data: {
            labels,
            datasets: [{
                label,
                data: values,
                borderColor: color,
                backgroundColor: color + '18',
                borderWidth: 1.5,
                pointRadius: values.length > 60 ? 0 : 2,
                pointHoverRadius: 5,
                fill: true,
                tension: 0.3
            }]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            animation: false,
            plugins: {
                legend: { display: false },
                tooltip: {
                    callbacks: {
                        title: items => items[0].label,
                        label: item => {
                            const count = item.parsed.y;
                            if (count === 0) return '  Servidor vacío';
                            return `  ${count} jugador${count !== 1 ? 'es' : ''}`;
                        },
                        afterBody: items => {
                            const idx = items[0].dataIndex;
                            const names = playerNames && playerNames[idx];
                            if (!names || names.length === 0) return [];
                            return ['', ...names.map(n => '  · ' + n)];
                        }
                    },
                    padding: 10,
                    displayColors: false
                }
            },
            scales: _spScales('')
        }
    });
};
