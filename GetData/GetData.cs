using System;
using System.Linq;
using System.IO;
using System.Globalization;
using System.Collections.Generic;

namespace GetData {
    class FileHelper {
        public string FileName;
        public DateTime StartDate;
    }

    class Opts {
        public bool IsFile = false;
        public string File;
        public DirectoryInfo Directory;
        public DateTime StartTime;
        public DateTime EndTime;
        public string FnameParseString = @"yyyy.MM.dd";
        public string DateTimeParseString;
        public char Fsep = ';';
        public string FileMask = "*";
        public int FieldIndex = 0;
    }

    class GetData {
        /*
         * TO DO
         *  - Add options set datetime parsestring for timestamp field
         *          -p 
         *  - Read from stdin if no files given?
         *  
         * Allow RegEx to extract date-part of filename?
         * Allow using timestamp of file rather than name? 
         * Allow time specifier m months w weeks d days h hours m s etc?
         * 
         */

        /*
         * Get last n hours data from logfiles. Assume logfiles are named as 
         * String.Format("ppsmon {0}.{1,1:D2}.{2,1:D2}.csv", logfiledate.Year, logfiledate.Month, logfiledate.Day);
         */
        static void Usage() {
            string usage =
@"Usage:
GetData (-t <hours>|-b <begin> [-e <end>]) -i (<folder>|<file>) [-f <format>] [-s <sep>]
    -t <hours>      Timespan. Number of hours to get, counting backwards from now. 
    -b <begin>      Begin time. Datetime; ""10/01/2017 22:13:00""
    -e <end>        End time. Datetime; ""10/01/2017 22:43:00"". Default now.
    -i <folder>     Input. Folder to read files from, or file to get data from. Required
    -f <format>     Formatstring to parse date from filename. Default 'yyyy.MM.dd'
                    Used when dir is given.
    -s <sep>        Separator. Character separating fields. Default ';'";

            Console.WriteLine(usage);
            Environment.Exit(0);
        }

        static Opts opts = new Opts();

        static void processFile(string f) {

            // Read and output lines falling in the timeframe requested
            var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            StreamReader fr = new StreamReader(fs);

            while (!fr.EndOfStream) {
                try {
                    string line = fr.ReadLine();
                    string[] words = line.Split(opts.Fsep);

                    // Skip lines without a valid timestamp
                    DateTime timestamp;
                    if (!DateTime.TryParse(words[opts.FieldIndex], out timestamp))
                        continue;

                    // Skip data older than from
                    if (timestamp < opts.StartTime)
                        continue;

                    // Skip data newer than to
                    if (timestamp > opts.EndTime)
                        break;

                    Console.WriteLine(line);
                } catch (Exception e) {
                    Console.Error.WriteLine("{0} Exception: {1}", DateTime.UtcNow, e.ToString());
                }
            }

            fr.Close();
        }

        static void Main(string[] args) {

            if (args.Length < 2)
                Usage();

            // Set defaults
            opts.EndTime = DateTime.Now;

            //
            // Parse cmd-line options
            //
            int argPtr = 0;
            while (argPtr < args.Length) {
                switch (args[argPtr]) {

                    case "-i":
                        if (File.Exists(args[++argPtr])) {
                            opts.IsFile = true;
                            opts.File = args[argPtr];
                        } else {
                            // Check for wildcards; "\path\to\logfiles\log*.csv"
                            if (args[argPtr].Contains("*") || args[argPtr].Contains("?")) {
                                string tmpstr = Path.GetFileName(args[argPtr]);
                                if (tmpstr.Length > 0)
                                    opts.FileMask = tmpstr;

                                opts.Directory = new DirectoryInfo(Path.GetDirectoryName(args[argPtr]));
                            } else {
                                opts.Directory = opts.Directory = new DirectoryInfo(args[argPtr]);
                            }

                            if (!opts.Directory.Exists) {
                                Console.Error.WriteLine("Error! File or directory not found: {0}", opts.Directory.FullName);
                                return;
                            }
                        }
                        break;

                    case "-t":
                        int hours = int.Parse(args[++argPtr]);
                        opts.StartTime = DateTime.UtcNow.AddHours(-hours);
                        opts.EndTime = DateTime.UtcNow;
                        break;

                    case "-f":
                        opts.FnameParseString = args[++argPtr];
                        break;

                    case "-s":
                        opts.Fsep = args[++argPtr][0];
                        break;

                    case "-b":
                        if (!DateTime.TryParse(args[++argPtr], out opts.StartTime)) {
                            Console.Error.WriteLine("Unable to parse begin-time {0}", args[argPtr]);
                            Environment.Exit(-1);
                        }
                        break;

                    case "-e":
                        if (!DateTime.TryParse(args[++argPtr], out opts.EndTime)) {
                            Console.Error.WriteLine("Unable to parse end-time {0}", args[argPtr]);
                            Environment.Exit(-1);
                        }
                        break;
                }
                argPtr++;
            }

            if ((!opts.IsFile && opts.Directory == null) || opts.StartTime == null)
                Usage();

            if (opts.IsFile) {
                processFile(opts.File);
            } else {
                List<FileHelper> files = new List<FileHelper>();

                foreach (string f in Directory.EnumerateFiles(opts.Directory.FullName, opts.FileMask)) {
                    DateTime fileDate;
                    string shortName = Path.GetFileNameWithoutExtension(f);
                    if (!DateTime.TryParseExact(shortName, opts.FnameParseString, null, DateTimeStyles.None, out fileDate)) {
                        Console.Error.WriteLine("Unable to parse date from filename '{0}'", shortName);
                        continue;
                    }

                    files.Add(new FileHelper { StartDate = fileDate, FileName = f });
                }

                // Sort files from older to newer
                files.Sort((f1, f2) => f1.StartDate.CompareTo(f2.StartDate));

                // Start processing on the last file older than StartTime
                for (int i = 0; i < files.Count; i++) {

                    // Skip files newer than EndTime
                    if (files[i].StartDate > opts.EndTime)
                        break;

                    // Check if this is the last file
                    if (i + 1 < files.Count) {
                        if (files[i + 1].StartDate <= opts.StartTime)  // If the next file is also older, skip this file
                            continue;
                    }

                    processFile(files[i].FileName);
                }
            }
        }
    }
}
