using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;
using System.IO;
using System.Globalization;

namespace EFOSView {
    public partial class MainForm : Form {

        static AutoResetEvent doneCopyingCharts = new AutoResetEvent(false);

        List<Chart> exportCharts = new List<Chart>();

        private void CopyCharts() {
            foreach (Chart c in charts) {
                System.IO.MemoryStream myStream = new System.IO.MemoryStream();

                Chart copyChart = new Chart();
                c.Serializer.Save(myStream);
                copyChart.Serializer.Load(myStream);
                copyChart.Visible = false;
                copyChart.Width = 1920;
                copyChart.Height = 1200;

                // Increase font-size of title and legends.
                copyChart.Titles[0].Font = new System.Drawing.Font(c.Titles[0].Font.FontFamily, 28);
                //copyChart.Legends[0].Font = new System.Drawing.Font(c.Legends[0].Font.FontFamily, 14);
                foreach (LegendItem l in copyChart.Legends[0].CustomItems)
                    l.Cells[1].Font = new System.Drawing.Font(c.Legends[0].Font.FontFamily, 14);

                copyChart.ChartAreas[0].Axes[1].TitleFont = new System.Drawing.Font(c.ChartAreas[0].Axes[1].TitleFont.FontFamily, 14);
                copyChart.ChartAreas[0].Axes[3].TitleFont = new System.Drawing.Font(c.ChartAreas[0].Axes[1].TitleFont.FontFamily, 14);

                exportCharts.Add(copyChart);
            }

            // Signal UI-thread we're done copying charts
            doneCopyingCharts.Set();

        }


        private bool running = false;
        private void ExportCharts() {
            if (running)
                return;

            running = true;

            if (exportCharts.Count == 0)
                CopyCharts();

            DateTime startTime;
            DateTime stopTime;
            List<EFOSDataPoint> data;

            if (exportHistoricPlots) {
                startTime = d.GetFirstDate();
                stopTime = new DateTime(startTime.Year, startTime.Month + 1, 1, 0, 0, 0); // Should be 00:00:00 day 1 of next month

                // Monthly plots
                
                while (stopTime < DateTime.UtcNow) {
                    data = null;

                    foreach (Chart c in exportCharts) {
                        string fname = String.Format("Monthly {0} {1} {2}.png", c.Titles[0].Text, startTime.Year, startTime.Month);
                        string fullPath = Path.Combine(plotPath, fname);

                        if (File.Exists(fullPath))
                            continue;

                        if (data == null)
                            data = GetData(startTime, stopTime, true, true);

                        BindChart(data, c, true, false);
                        c.SaveImage(fullPath, ChartImageFormat.Png);
                    }

                    startTime = stopTime;
                    stopTime = stopTime.AddMonths(1);
                }

                // Weekly plots
                GregorianCalendar cal = new GregorianCalendar();

                startTime = d.GetFirstDate();
                startTime = startTime.AddDays((int)DayOfWeek.Monday - (int)startTime.DayOfWeek);
                stopTime = startTime.AddDays(7);
                while (stopTime < DateTime.UtcNow) {
                    data = null;

                    foreach (Chart c in exportCharts) {
                        string fname = String.Format("Weekly {0} {1} w {2}.png", c.Titles[0].Text, startTime.Year, cal.GetWeekOfYear(startTime, CalendarWeekRule.FirstFullWeek, DayOfWeek.Monday));
                        string fullPath = Path.Combine(plotPath, fname);

                        if (File.Exists(fullPath))
                            continue;

                        if (data == null)
                            data = GetData(startTime, stopTime, true, true);

                        BindChart(data, c, true, false);
                        c.SaveImage(fullPath, ChartImageFormat.Png);
                    }

                    startTime = stopTime;
                    stopTime = stopTime.AddDays(7);
                }

                // Daily plots
                startTime = d.GetFirstDate();
                stopTime = startTime.AddDays(1);

                while (stopTime < DateTime.UtcNow) {
                    data = null;

                    foreach (Chart c in exportCharts) {
                        string fname = String.Format("Daily {0} {1}-{2}-{3}.png", c.Titles[0].Text, startTime.Year, startTime.Month, startTime.Day);
                        string fullPath = Path.Combine(plotPath, fname);

                        if (File.Exists(fullPath))
                            continue;

                        if (data == null)
                            data = GetData(startTime, stopTime, true, true);

                        BindChart(data, c, true, false);
                        c.SaveImage(fullPath, ChartImageFormat.Png);
                    }

                    startTime = stopTime;
                    stopTime = stopTime.AddDays(1);
                }
            }

            // Current Monthly, weekly and Daily plots

            // Monthly plots
            stopTime = DateTime.UtcNow;
            startTime = stopTime.AddMonths(-1);

            data = GetData(startTime, stopTime, true, true);

            foreach (Chart c in exportCharts) {
                string fname = String.Format("Monthly {0} current.png", c.Titles[0].Text);
                string fullPath = Path.Combine(plotPath, fname);

                BindChart(data, c, true, false);
                c.SaveImage(fullPath, ChartImageFormat.Png);
            }

            // Weekly plots
            stopTime = DateTime.UtcNow;
            startTime = stopTime.AddDays(-7);

            data = GetData(startTime, stopTime, true, true);
            
            foreach (Chart c in exportCharts) {
                string fname = String.Format("Weekly {0} current.png", c.Titles[0].Text);
                string fullPath = Path.Combine(plotPath, fname);

                BindChart(data, c, true, false);
                c.SaveImage(fullPath, ChartImageFormat.Png);
            }

            //Daily plots
            stopTime = DateTime.UtcNow;
            startTime = stopTime.AddDays(-1);

            data = GetData(startTime, stopTime, true, true);

            foreach (Chart c in exportCharts) {
                string fname = String.Format("Daily {0} current.png", c.Titles[0].Text);
                string fullPath = Path.Combine(plotPath, fname);

                BindChart(data, c, true, false);
                c.ResetAutoValues();
                c.SaveImage(fullPath, ChartImageFormat.Png);
            }

            data = null;

            running = false;
        }
    }
}
