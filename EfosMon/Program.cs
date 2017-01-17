using System;
using System.Text;
using System.Threading;
using System.IO.Ports;
using System.IO;
using System.Linq;

namespace EfosMon {
    class EFOSpoller : IDisposable {

        #region Defs
        static string[] queries = {
            "D00",
            "D01",
            "D02",
            "D03",
            "D04",
            "D05",
            "D06",
            "D07",
            "D08",
            "D09",
            "D10",
            "D11",
            "D12",
            "D13",
            "D14",
            "D15",
            "D16",
            "D17",
//            "D18",
//            "D19",
            "D20",
            "D21",
            "D22",
            "D23",
            "D24",
            "D25",
            "D26",
            "D27",
            "D28",
            "D29",
            "D30",
            "D31",
            "D32",
            "D33",
            "D34"
        };

        static string[] names = {
            "U (input A) [V]     ",
            "I (input A) [A]     ",
            "U (input B) [V]     ",
            "I (input B) [A]     ",
            "T (source) [C]      ",
            "Set H press. [V]    ",
            "Read H press. [V]   ",
            "Palladium heat. [V] ",
            "LO heat. [V]        ",
            "UO heat. [V]        ",
            "Dalle heat. [V]     ",
            "LI heat. [V]        ",
            "UI heat. [V]        ",
            "Cavity heat. [V]    ",
            "T (cavity) [C]      ",
            "T (ambient) [C]     ",
            "Cavity var. [V]     ",
            "C field [uA]        ",
 //           "int. N°2 HT U [kV]  ",
 //           "int. N°2 HT I [µA]  ",
            "Int. Vac U [kV]     ",
            "Int. Vac I [uA]     ",
            "Ext. Vac U [kV]     ",
            "Ext. Vac I [uA]     ",
            "RF U [V]            ",
            "RF I [A]            ",
            "+24 VDC [V]         ",
            "+15 VDC [V]         ",
            "-15 VDC [V]         ",
            "+5 VDC [V]          ",
            "+15 VDC [V]         ",
            "-15 VDC [V]         ",
            "OCXO var. [V]       ",
            "Ampl. 5.7 kHz [V]   ",
            "Lock                "
        };

        static double[] scale = {
            0.230,
            0.096,
            0.230,
            0.096,
            0.960,
            0.096,
            0.096,
            0.192,
            0.192,
            0.192,
            0.192,
            0.192,
            0.192,
            0.192,
            0.010,
            0.096,
            0.096,
            1.920,
//            0.048,
//            19.00,
            0.048,
            19.00,
            0.048,
            19.00,
            0.298,
            0.010,
            0.240,
            0.148,
            0.148,
            0.048,
            0.148,
            0.148,
            0.078,
            0.078,
            1
        };

        static double[] offsets = {
            0,
            0,
            0,
            0,
            -1.1,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            26,
            0,
//            0,
//            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0
        };
        #endregion

        StreamWriter OpenLogFile() {
            DateTime now = DateTime.Now;

            logfiledate = now.Date;
            string filename = String.Format("efos3 {0}.{1,1:D2}.{2,1:D2}.csv", now.Year, now.Month, now.Day);
            logfilename = Path.Combine(logPath, filename);

            bool writeHeaders = false;

            FileInfo f = new FileInfo(logfilename);
            if (!f.Exists)
                writeHeaders = true;

            //if (!File.Exists(logfilename));  // Todo Returns false even when file exist.
            //    writeHeaders = false;

            StreamWriter log = new StreamWriter(logfilename, true, Encoding.ASCII);
            log.AutoFlush = true;

            if (writeHeaders) {
                log.Write("Timestamp;");

                for (int i = 0; i < names.Length; i++)
                    log.Write("{0};", names[i].Trim());

                log.WriteLine("Errors");
            }

            flushCounter = 30;

            return log;
        }

        int secs = 10;           // Elapsed number of seconds
        int pollInterval = 10;  // 10 second poll-interval

        double[] values = new double[queries.Length];
        bool[] parseErrors = new bool[queries.Length];  // Flag parse errors

        StreamWriter log;
        uint flushCounter = 30;     // 5-minute intervals

        SerialPort efos = new SerialPort("com3");

        // Represent the current logfile
        DateTime logfiledate;
        string logfilename;
        public string logPath="";

        public static object executionLock = new object();

        public void Poll(Object stateInfo) {

            if (++secs >= pollInterval) {
                lock (executionLock) {
                    secs = 0;

                    string s;

                    for (int i = 0; i < queries.Length; i++) {

                        efos.Write(queries[i]);
                        s = efos.ReadLine().Trim();
                        s = (string)s.Substring(3); // Skip echoed query

                        parseErrors[i] = false;

                        double val;

                        try {
                            val = (double)int.Parse(s, System.Globalization.NumberStyles.HexNumber);

                            if (i < 30)
                                val -= 128;

                            val *= scale[i];
                            val += offsets[i];

                            values[i] = val;
                        } catch (Exception) {

                            // Leave old value unchanged, flag error on console
                            parseErrors[i] = true;
                        }
                    }

                    DateTime now = DateTime.Now;
                    if (now.Date != logfiledate) {
                        if (log != null) {
                            log.Close();
                            log = null;
                        }
                    }

                    if (log == null)
                        log = OpenLogFile();

                    log.Write("{0};", now);

                    Console.Clear();

                    // Write to logfile and console in the same loop.
                    for (int i = 0; i < queries.Length; i++) {
                        Console.WriteLine("{0}{1}{2}", names[i], String.Format("{0,8:##0.00}", values[i]), parseErrors[i] ? " *" : "");
                        log.Write(values[i]);
                        log.Write(";");
                    }

                    // Report parse-errors in the log-file
                    log.WriteLine(parseErrors.Contains(true) ? 1 : 0);

                    Console.Write("{0} ", flushCounter);

                    // Close the logfile every 10 minutes, to let DropBox pick it up
                    if (flushCounter-- == 0) {
                        log.Close();
                        log = null;
                    }
                }
            } else {
                Console.Write(".");
            }
        }

        public EFOSpoller() {
            efos.BaudRate = 9600;
            efos.Open();
        }

        private bool disposed = false;

        public void Dispose() {
            if (disposed)
                return;

            disposed = true;

            if (log != null) {
                log.Close();
                log.Dispose();
            }

            if (efos != null) {
                efos.Close();
                efos.Dispose();
            }
        }
    }

    class Program {

        static public AutoResetEvent done = new AutoResetEvent(false);

        static EFOSpoller poller = new EFOSpoller();

        static Timer timer;

        static void Main(string[] args) {
            // For registry access
            const string userRoot = "HKEY_CURRENT_USER";
            const string subkey = "EFOSView\\";
            const string keyName = userRoot + "\\" + subkey;
            string value = "LogPath";

            Console.CancelKeyPress += Console_CancelKeyPress;

            // Get  directory from args, if any
            string logPath;
            if(args.Count() > 0) {
                logPath = args[0];

                if (Directory.Exists(logPath)) 
                    Microsoft.Win32.Registry.SetValue(keyName, value, logPath);
            } else {
                logPath = (string)Microsoft.Win32.Registry.GetValue(keyName, value, "");
            }

            if (!Directory.Exists(logPath)) {
                Console.Error.WriteLine("Error! Could not open LogDirectory {0}. Using .\\", logPath);
            }else {
                poller.logPath = logPath;
            }

            // Call poller every second
            timer = new Timer(poller.Poll, done, 0, 1000);

            // Wait untill Ctrl-C is pressed.
            done.WaitOne();

            // Wait for any ongoing execution of the poller to finish
            lock (EFOSpoller.executionLock) {
                timer.Dispose();
            }

            poller.Dispose();

            done.Dispose();
        }

        private static void Console_CancelKeyPress(object sender, ConsoleCancelEventArgs e) {
            e.Cancel = true;

            done.Set();
        }
    }
}