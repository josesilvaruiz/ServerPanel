window._spCharts = {};

window.spRenderChart = function (id, labels, values, label, color, ySuffix) {
    const canvas = document.getElementById(id);
    if (!canvas) return;

    if (window._spCharts[id]) {
        window._spCharts[id].destroy();
        delete window._spCharts[id];
    }

    const ctx = canvas.getContext('2d');

    window._spCharts[id] = new Chart(ctx, {
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
                tooltip: {
                    callbacks: {
                        label: ctx => `${ctx.parsed.y}${ySuffix}`
                    }
                }
            },
            scales: {
                x: {
                    ticks: {
                        color: '#64748b',
                        font: { size: 10 },
                        maxTicksLimit: 8,
                        maxRotation: 0
                    },
                    grid: { color: '#1e293b' }
                },
                y: {
                    ticks: {
                        color: '#64748b',
                        font: { size: 10 },
                        callback: v => v + ySuffix
                    },
                    grid: { color: '#1e293b' },
                    beginAtZero: true
                }
            }
        }
    });
};
