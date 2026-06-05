namespace ServerPanel.Models;

public record MetricsChartData(
    string[] Labels,
    double[] Cpu,
    double[] Ram,
    double[] Load
);
