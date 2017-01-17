using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Globalization;

namespace EFOSView {
    public enum DataSet {
        PowerInputs,
        PowerSupplies,
        Hydrogen,
        Heaters,
        IonPumps,
        Temperature,
        RF,
        Cavity,
        Amplitude
    }

    public enum EFOScol {
        InputAV,
        InputAA,
        inputBV,
        InputBA,
        Tsource,
        SetH,
        ReadH,
        PalladiumHeat,
        LOHeat,
        UOHeat,
        DalleHeat,
        LIHeat,
        UIHeat,
        CavHeat,
        CavTemp,
        Tamb,
        CavVar,
        Cfield,
        IntVacV,
        IntVacA,
        ExtVacV,
        ExtVacA,
        RFV,
        RFA,
        p24V,
        p15V1,
        n15V1,
        p5V,
        p15V2,
        n15V2,
        OCXOV,
        Amp5kHz,
        Lock,
        Errors
    }

    public class EFOSDataPoint {
        public double timestamp;
        public double[] values;
    }

    class DataLoader {
        public string path;

        /*
         * Used to signal the loader that new data is available. Add newly acquired data to the global dataset.
         */
        public void Refresh() {

        }

        /*
         * LoadData: Loads data into memory.
         */
        delegate double transform(double val);
        public List<EFOSDataPoint> LoadData(DateTime from, DateTime to) {
            List<EFOSDataPoint> data = new List<EFOSDataPoint>();

            string[] files = Directory.EnumerateFiles(path, "EFOS3 20*.csv").ToArray();
            Array.Sort(files);                                                              // Sort from oldest to newest

            // Need to strip off hour:in:sec from "from", in order to load the file containing that data.
            DateTime fromDate = DateTime.Parse(from.ToShortDateString());

            foreach (string file in files) {
                DateTime currentFile = DateTime.Parse(file.Substring(file.Length - 14, 10));

                // Filter out files older than "from"
                if (currentFile < fromDate)
                    continue;

                // Skip datafiles newer than "to".
                if (currentFile > to)
                    continue;
                
                var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                StreamReader f = new StreamReader(fs);

                // Read and discard first line, headers
                //f.ReadLine();

                while (!f.EndOfStream) {
                    try {
                        EFOSDataPoint d = new EFOSDataPoint();

                        string[] words = f.ReadLine().Split(';');

                        // First column is timestamp.
                        //d.timestamp = DateTime.Parse(words[0]).ToOADate();

                        //Skip lines where last field (parse error) is not 0
                        if (words[words.Length - 1] != "0")
                            continue;

                        // Skip lines not starting with a valid timestamp - headers
                        DateTime tmp;
                        if (!DateTime.TryParse(words[0], out tmp))
                            continue;

                        d.timestamp = tmp.ToOADate();

                        // Skip data older than from
                        if (d.timestamp <= from.ToOADate())
                            continue;

                        // Skip data newer than to
                        if (to != null && d.timestamp > to.ToOADate())
                            continue;

                        // The rest is doubles. Transform
                        transform t;
                        double[] values = new double[words.Length - 2]; // Subract timestamp, and last field (error-flag)
                        for (int i = 0; i < values.Length; i++) {

                            switch (i) {
                                case (int)EFOScol.IntVacA:
                                case (int)EFOScol.IntVacV:
                                case (int)EFOScol.ExtVacA:
                                case (int)EFOScol.ExtVacV:
                                    t = val => Math.Abs(val);
                                    break;
                                case (int)EFOScol.p24V:
                                    t = val => val - 24;
                                    break;
                                case (int)EFOScol.p15V1:
                                case (int)EFOScol.p15V2:
                                    t = val => val - 15;
                                    break;
                                case (int)EFOScol.n15V1:
                                case (int)EFOScol.n15V2:
                                    t = val => val + 15;
                                    break;
                                case (int)EFOScol.p5V:
                                    t = val => val - 5;
                                    break;
                                default:
                                    t = val => val;
                                    break;
                            }
                            values[i] = t(double.Parse(words[i + 1], NumberStyles.Float|NumberStyles.AllowDecimalPoint|NumberStyles.AllowLeadingSign));
                        }

                        d.values = values;

                        data.Add(d);
                    } catch (Exception) {
                         // TO do - log
                    }
                }

                f.Close();
            }

            // The last point read may be corrupted, as another process is writing to the file. Try to detect, and discard.
            if (data.Count > 0) {
                EFOSDataPoint last = data.Last();
                while (data.Count > 0 && last.values.Length != 33) {
                    data.Remove(last);
                    last = data.Last();
                }
            }

            return data;
        }

        // Rturns first date with data
        public DateTime GetFirstDate() {
            string[] files = Directory.EnumerateFiles(path, "EFOS3 20*.csv").ToArray();
            if (files.Length == 0)
                return DateTime.Now;

            Array.Sort(files);
            return DateTime.Parse(files[0].Substring(files[0].Length - 14, 10));
        }
        /*
         * The constructor does not load data by default.
         */
        public DataLoader(string path = @"C:\") {
            this.path = path;
        }
    }
}
