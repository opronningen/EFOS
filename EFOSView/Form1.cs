using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;
using System.IO;
using IniParser;
using IniParser.Model;

namespace EFOSView {
    public partial class MainForm : Form {
        private DataLoader d;
        //private List<SeriesCollection> seriescollections = new List<SeriesCollection>();
        private List<Chart> charts = new List<Chart>();

        // Set our custom colors on all charts
        Color[] palette = new Color[]{
                Color.FromArgb(0,0,255),
                Color.FromArgb(16,130,16),
                Color.FromArgb(255,0,0),
                Color.FromArgb(0,192,255),
                Color.FromArgb(187,131,0),
                Color.FromArgb(0,200,0),
                Color.FromArgb(168,23,186),
                Color.FromArgb(255,0,255)
            };

        private string logPath;
        private string plotPath;
        private bool exportPlots = false;
        private bool exportHistoricPlots = false;

        public MainForm() {
            InitializeComponent();

            fileSystemWatcher1.EnableRaisingEvents = false;

            // For convenience.
            charts.Add(HeatersChart);
            charts.Add(RFChart);
            charts.Add(CavityChart);
            charts.Add(PowerInputChart);
            charts.Add(PowerSuppliesChart);
            charts.Add(HydrogenChart);
            charts.Add(IonPumpsChart);
            charts.Add(PllChart);
            charts.Add(TemperatureChart);

            foreach (Chart c in charts) {
                //c.ChartAreas[0].CursorX.LineColor = Color.Red;
                c.ChartAreas[0].CursorX.SelectionColor = Color.Red;
                c.ChartAreas[0].CursorX.IsUserEnabled = true;
                c.ChartAreas[0].CursorX.IsUserSelectionEnabled = true;
                c.ChartAreas[0].CursorX.IntervalType = DateTimeIntervalType.Hours;
                c.ChartAreas[0].AxisX.ScaleView.Zoomable = false;
                
                c.SelectionRangeChanged += C_SelectionRangeChanged;

                for (int i = 0; i < c.Series.Count; i++) {
                    c.Series[i].Color = palette[i];
                    // Below - test
                    c.Series[i].IsVisibleInLegend = false;

                    LegendItem l = new LegendItem(c.Series[i].Name, c.Series[i].Color, "");
                    l.Cells.Add(new LegendCell(LegendCellType.SeriesSymbol, ""));
                    LegendCell lc = new LegendCell(LegendCellType.Text, c.Series[i].Name);
                    lc.Font = c.Legends[0].Font;
                    l.Cells.Add(lc);

                    l.ImageStyle = LegendImageStyle.Line;
                    c.Legends[0].CustomItems.Add(l);
                }
            }

            statusLabel.Text = string.Format("Updated {0}", DateTime.Now.ToLongTimeString());

            var parser = new FileIniDataParser();
            IniData iniData = parser.ReadFile("EFOS.ini");
            plotPath = iniData["EfosView"]["plot-path"];
            logPath = iniData["EfosMon"]["data-path"];
            exportPlots = bool.Parse(iniData["EfosView"]["export-plots"]);
            exportHistoricPlots = bool.Parse(iniData["EfosView"]["export-historic-plots"]);

            d = new DataLoader(logPath);

            // Run exportcharts in the background
            if (exportPlots) {
                Task.Run(() => ExportCharts());

                // Wait untill ExportCharts has copied the charts
                doneCopyingCharts.WaitOne();
            }

            var data = GetData(DateTime.Now.AddHours(-span), DateTime.Now, true);
            BindData(data, true);

            fileSystemWatcher1.Path = logPath;

            fileSystemWatcher1.EnableRaisingEvents = true;
        }

   
        /*
        * BindData: Sets the SeriesCollection.Points to the loaded data. Optionally preserve the data already there.
        */

        private void BindChart(List<EFOSDataPoint> data, Chart chart, bool invalidateData, bool tracking) {
            if (data.Count < 2)
                return;

            chart.SuspendLayout();

            foreach (Series s in chart.Series) {
                if (invalidateData) {
                    while (s.Points.Count > 0)
                        s.Points.RemoveAt(0);
                } else if (tracking) {
                    // If we are in trackingMode, remove older data
                    double oldest = DateTime.Now.AddHours(-span).ToOADate();

                    while (s.Points.Count != 0 && s.Points[0].XValue < oldest)
                        s.Points.RemoveAt(0);
                }

                //s.Points.Clear(); // SLOOOOOOW!!!!

                // Add new datapoints.
                int index = int.Parse(s["index"]);
                foreach (EFOSDataPoint d in data)
                    s.Points.AddXY(d.timestamp, d.values[index]);

                // Set axis-type based on timespan displayed.
                ChartValueType axis;

                double timeSpan;
                timeSpan = (DateTime.FromOADate(data.Last().timestamp) - DateTime.FromOADate(chart.Series[0].Points[0].XValue)).TotalHours;

                if (timeSpan > 48)
                    axis = ChartValueType.DateTime;
                else
                    axis = ChartValueType.Time;

                s.XValueType = axis;
            }

            chart.ResetAutoValues();
            chart.ResumeLayout();
        }

        private bool trackingMode = true;   // If true, update display as new data comes in. If false, display only a fixed interval
        private int span = 24;              // Number of hours to display in trackingMode. 
        private int averagingFactor = 1;    // Appriximate..

        private void BindData(List<EFOSDataPoint> data, bool invalidateData) {

            // Add data to each series
            foreach (Chart c in charts) {
                BindChart(data, c, invalidateData, trackingMode);
            }
        }

        /*
         * Scenario
         *  1. Called from UI-thread, on startup or change of timescale. invalidateData = true, isBackgrounThread = false
         *      -  calculate and store averaging factor
         *  2. Called from bakgroundthread when new data arrives. invalidateData = false, isBackgroundThread = false
         *      - recall averaging factor
         *  3. Called from backgroundthread to produce plots on disk. invalidateData = false, isBackgroundThread = true
         *      - calculate, do NOT store averaging factor
         */
        private List<EFOSDataPoint> GetData(DateTime from, DateTime to, bool invalidateData, bool isBackgroundThread = false) {
            List<EFOSDataPoint> data = d.LoadData(from, to);

            if (data.Count == 0)
                return data;

            int avg = 0;

            // Average date to around 2000 points. Charts are slow with too many points.
            // Bug: If requesting more data than is available, averagingFactor will be incorrectly set.
            // Not expected to happen..
            string msg;
            if (invalidateData) {
                avg = (int)Math.Floor((double)data.Count / 2000);

                msg = String.Format("Averaging {0} pts", avg);

                if (!isBackgroundThread) {
                    if (InvokeRequired) {
                        Invoke((MethodInvoker)delegate { averagingLabel.Text = msg; });
                    } else {
                        averagingLabel.Text = msg;
                    }

                    averagingFactor = avg;
                }

            } else {
                avg = averagingFactor;
            }
            
            //if (data.Count < averagingFactor)
            //    return;

            // averagingFactor is preserved from the first load, any updated data
            // will use the same factor.
            if (avg > 1) {

                List<EFOSDataPoint> averagedData = new List<EFOSDataPoint>();

                int i = 0;
                while (i < data.Count - 1) {
                    EFOSDataPoint avPt = new EFOSDataPoint();
                    int cnt = 0;

                    while (cnt < avg) {
                        if (i + cnt > data.Count - 1)
                            goto Foo;   // Break out, ensuring that the last "incomplete" datapoint is not added to collection.

                        EFOSDataPoint dataPt = data[i + cnt];
                        avPt.timestamp = dataPt.timestamp;          // the timestamp of the last datapoint will be the 
                                                                    // "from" to retrieve updated data
                        if (cnt == 0)
                            avPt.values = dataPt.values;
                        else
                            for (int idx = 0; idx < avPt.values.Length; idx++)
                                avPt.values[idx] += dataPt.values[idx];

                        cnt++;
                    }

                    // Average
                    for (int idx = 0; idx < avPt.values.Length; idx++)
                        avPt.values[idx] /= cnt;

                    i += cnt;

                    averagedData.Add(avPt);
                }
            Foo:

                // Do not update from background thread.
                if (!isBackgroundThread) {
                    msg = string.Format("Received {0} pts, plotted {1} pts, left {2} pts", data.Count, averagedData.Count, data.Count - (averagedData.Count * averagingFactor));
                    if (InvokeRequired) {
                        Invoke((MethodInvoker)delegate { debugLabel.Text = msg; });
                    } else {
                        debugLabel.Text = msg;
                    }
                }
                data = averagedData;
            } else {
                // Do not update from background thread.
                if (!isBackgroundThread) {
                    msg = string.Format("Received {0} pts", data.Count);
                    if (InvokeRequired) {
                        Invoke((MethodInvoker)delegate { debugLabel.Text = msg; });
                    } else {
                        debugLabel.Text = msg;
                    }
                }
            }

            return (data);
        }

        private void fileSystemWatcher1_Changed(object sender, System.IO.FileSystemEventArgs e) {
            if (!trackingMode)
                return;

            // Ask for all data newer than the last point in any of the series.
            List<EFOSDataPoint> data = GetData(DateTime.FromOADate(HydrogenChart.Series[0].Points.Last().XValue), DateTime.Now, false);

            string msg = string.Format("Updated {0}", DateTime.Now.ToLongTimeString());
            Invoke((MethodInvoker)delegate {
                BindData(data, false);
                statusLabel.Text = msg;
            });

            // Run exportcharts in the background
            if (exportPlots) {
                Task.Run(() => ExportCharts());
            }
        }

        /*
         * Chart_Click: Handle zoom/unzoom, and enable/disable series
         */
        private bool zoomed = false;
        private Chart zoomedChart;
        private void Chart_Click(object sender, EventArgs e) {
            var args = e as MouseEventArgs;
            var chart = sender as Chart;

            // On right-click, reset zoom, re-enable tracking mode
            if(args.Button == MouseButtons.Right) {
                trackingMode = true;

                // TODO: detect if correct data is already displayed. If so do not reload data
                var data = GetData(DateTime.Now.AddHours(-span), DateTime.Now, true);
                BindData(data, true);

                return;
            }

            // Detect if this is the ass-end of a selection. If so, do not switch view modes
            var c = chart.ChartAreas[0].CursorX;
            if (c.SelectionStart != double.NaN && !c.SelectionEnd.Equals(c.SelectionStart))
                return;

            c.Position = double.NaN;

            // Check if a legend was clicked. If so, toggle visibility of series. 
            HitTestResult target = chart.HitTest(args.X, args.Y);
            if (target.Object is LegendItem) {
                LegendItem leg = (LegendItem)target.Object;

                if (chart.Series[leg.Name].Enabled) {
                    chart.Series[leg.Name].Enabled = false;
                    leg.Cells[0].BackColor = Color.LightGray;
                    leg.Cells[1].BackColor = Color.LightGray;
                    leg.Cells[1].ForeColor = Color.Gray;
                } else {
                    chart.Series[leg.Name].Enabled = true;
                    leg.Cells[0].BackColor = Color.AliceBlue;
                    leg.Cells[1].BackColor = Color.AliceBlue;
                    leg.Cells[1].ForeColor = Color.Black;
                }

                chart.ChartAreas[0].RecalculateAxesScale();

                //if (chart.Series[leg.SeriesName].Color != Color.Transparent) {
                //    chart.Series[leg.SeriesName].Color = Color.Transparent;
                //} else {
                //    // Restore original color
                //    chart.Series[leg.SeriesName].Color = palette[chart.Series.IndexOf(chart.Series[leg.SeriesName])];
                //}
            } else {
                if (zoomed && zoomedChart.Equals(chart)) {
                    defaultView();
                } else {
                    zoomedView((Chart)sender);
                }
            }   
        }

        private void defaultView() {
            zoomed = false;

            tableLayoutPanel1.SuspendLayout();

            tableLayoutPanel1.Controls.Clear();
            tableLayoutPanel1.ColumnCount = 3;
            tableLayoutPanel1.ColumnStyles.Clear();
            tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33333F));
            tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33333F));
            tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33333F));
            tableLayoutPanel1.Controls.Add(HeatersChart, 0, 0);
            tableLayoutPanel1.Controls.Add(IonPumpsChart, 1, 0);
            tableLayoutPanel1.Controls.Add(PllChart, 2, 0);
            tableLayoutPanel1.Controls.Add(CavityChart, 0, 1);
            tableLayoutPanel1.Controls.Add(RFChart, 1, 1);
            tableLayoutPanel1.Controls.Add(HydrogenChart, 2, 1);
            tableLayoutPanel1.Controls.Add(TemperatureChart, 0, 2);
            tableLayoutPanel1.Controls.Add(PowerSuppliesChart, 1, 2);
            tableLayoutPanel1.Controls.Add(PowerInputChart, 2, 2);
            tableLayoutPanel1.RowCount = 3;
            tableLayoutPanel1.RowStyles.Clear();
            tableLayoutPanel1.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33333F));
            tableLayoutPanel1.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33333F));
            tableLayoutPanel1.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33333F));

            foreach (Chart c in charts) {
                tableLayoutPanel1.SetColumnSpan(c, 1);
                c.Legends[0].Enabled = true;
            }

            tableLayoutPanel1.ResumeLayout();
        }

        private void zoomedView(Chart chart) {
            zoomed = true;
            zoomedChart = chart;

            tableLayoutPanel1.SuspendLayout();

            tableLayoutPanel1.Controls.Clear();

            tableLayoutPanel1.ColumnCount = 4;
            tableLayoutPanel1.ColumnStyles.Clear();
            tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));

            tableLayoutPanel1.RowCount = 3;
            tableLayoutPanel1.RowStyles.Clear();
            tableLayoutPanel1.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
            tableLayoutPanel1.RowStyles.Add(new RowStyle(SizeType.Percent, 20));
            tableLayoutPanel1.RowStyles.Add(new RowStyle(SizeType.Percent, 20));

            // The zoomed chart
            chart.Legends[0].Enabled = true;
            tableLayoutPanel1.Controls.Add(chart, 0, 0);
            tableLayoutPanel1.SetColumnSpan(chart, 4);

            // The other charts
            int row = 1, col = 0;
            foreach (Chart c in charts) {
                if (c.Equals(chart))
                    continue;

                if (col > 3) {
                    col = 0;
                    row++;
                }

                c.Legends[0].Enabled = false;

                tableLayoutPanel1.Controls.Add(c, col++, row);
                tableLayoutPanel1.SetColumnSpan(c, 1);
            }

            tableLayoutPanel1.ResumeLayout();
        }

        private void trackingButton_DropDownItemClicked(object sender, ToolStripItemClickedEventArgs e) {
            if (e.ClickedItem.Text == "Tracking")
                trackingMode = true;
            else
                trackingMode = false;

            e.ClickedItem.OwnerItem.Text = e.ClickedItem.Text;
        }

        // Set new timespan based on user selection
        private void timespanButton_DropDownItemClicked(object sender, ToolStripItemClickedEventArgs e) {

            // Tag contains number of hours to display

            fileSystemWatcher1.EnableRaisingEvents = false;

            timespanButton.Text = "Timespan " + e.ClickedItem.Text;

            span = int.Parse((string)e.ClickedItem.Tag);

            //int idx = timespanButton.DropDownItems.IndexOf(e.ClickedItem);

            //switch (idx) {
            //    case 0: // TO-DO: Select ...
            //        span = 24;
            //        break;
            //    case 1: // 1 month
            //        span = 24*31;
            //        break;
            //    case 2: // 2 Weeks
            //        span = 14 * 24;
            //        break;
            //    case 3: // 1 Week
            //        span = 7 * 24;
            //        break;
            //    case 4: // 48 hours
            //        span = 48;
            //        break;
            //    case 5: // 24 hours
            //        span = 24;
            //        break;
            //    case 6: // 12 Hours
            //        span = 12;
            //        break;
            //    case 7: // 6 Hours
            //        span = 6;
            //        break;
            //    case 8: // 1 hour
            //        span = 1;
            //        break;
            //}

            // bind new data..
            var data = GetData(DateTime.Now.AddHours(-span), DateTime.Now, true);
            BindData(data, true);

            fileSystemWatcher1.EnableRaisingEvents = true;
        }

        private void C_SelectionRangeChanged(object sender, CursorEventArgs e) {

            if (e.NewSelectionStart.Equals(e.NewSelectionEnd) || e.NewSelectionStart == double.NaN || e.NewSelectionEnd == double.NaN)
                return;

            trackingMode = false;

            Chart c = sender as Chart;
            c.ChartAreas[0].CursorX.SetSelectionPosition(0, 0);
            c.ChartAreas[0].CursorX.Position = double.NaN;

            var data = GetData(DateTime.FromOADate(e.NewSelectionStart), DateTime.FromOADate(e.NewSelectionEnd), true);
            BindData(data, true);

            return;
        }

    }
}
