using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ERN028_MakinaVeriTopalamaFormsApp
{
    using System;
    using System.Windows.Forms;
    using System.Windows.Forms.DataVisualization.Charting;

    public partial class GraphForm : Form
    {
        public GraphForm(double[] originalTimes, double[] originalValues, double[] newTimes, double[] newValues, List<Tuple<double, double>> gaps)
        {
            InitializeComponent();
            PlotData(originalTimes, originalValues, newTimes, newValues, gaps);
        }

        private void PlotData(double[] originalTimes, double[] originalValues, double[] newTimes, double[] newValues, List<Tuple<double, double>> gaps)
        {
            var chart = new Chart();
            chart.Dock = DockStyle.Fill;
            this.Controls.Add(chart);

            chart.ChartAreas.Add("ChartArea");

            var seriesOriginal = new Series
            {
                Name = "Original Data",
                ChartType = SeriesChartType.Point,
                ChartArea = "ChartArea"
            };
            var seriesSpline = new Series
            {
                Name = "Spline Interpolation",
                ChartType = SeriesChartType.Line,
                ChartArea = "ChartArea"
            };

            for (int i = 0; i < originalTimes.Length; i++)
            {
                seriesOriginal.Points.AddXY(originalTimes[i], originalValues[i]);
            }

            double previousTime = newTimes[0];
            for (int i = 0; i < newTimes.Length; i++)
            {
                if (gaps.Any(g => newTimes[i] > g.Item1 && newTimes[i] < g.Item2)) // Boşluk varsa kesik çizgi
                {
                    // Yeni bir seri oluştur ve devam et
                    seriesSpline = new Series
                    {
                        Name = "Spline Interpolation " + i,
                        ChartType = SeriesChartType.Line,
                        ChartArea = "ChartArea"
                    };
                    chart.Series.Add(seriesSpline);
                }
                seriesSpline.Points.AddXY(newTimes[i], newValues[i]);
                previousTime = newTimes[i];
            }
            chart.Series.Add(seriesOriginal);
            //chart.Series.Add(seriesSpline);
        }
    }
}
