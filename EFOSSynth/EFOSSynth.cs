using System;
using System.IO.Ports;
using System.IO;
using IniParser;
using IniParser.Model;

namespace EFOSSynth
{
    class EFOSSynth
    {
        
        static void Main(string[] args)
        {
            // Read ini-file
            var parser = new FileIniDataParser();
            IniData iniData = parser.ReadFile("EFOS.ini");

            string com = iniData["EfosSynth"]["com-port"];
            StreamWriter log = new StreamWriter(iniData["EfosSynth"]["log-file"], true);

            SerialPort efos = new SerialPort(com, 9600);

            int curPos = 32;            // Cursor-position
            const int minCurPos = 25;   // "Left-most" valid position
            const int maxCurPos = 32;   // "Right-most" valid postion
            const int dotPos = 27;
            double multiplier = 1E-5;
            string readBuffer;
            string sendBuffer;

            const double f = 1420405751.00;
            double curSynth = 5751.689;
            double newSynth = curSynth;
            double fDelta = 0;

            void ShowPrompt()
            {
                Console.Clear();
                Console.WriteLine("Current Synth setting: {0:F5}", curSynth);
                Console.WriteLine("New Synth setting:     {0:F5}", newSynth);
                Console.SetCursorPosition(0, 2);
                ShowFrqOffset();
                Console.SetCursorPosition(curPos, 1);
            }

            void ShowFrqOffset()
            {
                fDelta = (curSynth - newSynth) / f;
                Console.Write("Frequency offset:     {0}        ", fDelta.ToString("+0.000E+00;-0.000E+00"));
            }

            void getCurrentSynth()
            {
                if(!efos.IsOpen)
                    efos.Open();

                efos.Write("F");
                readBuffer = efos.ReadLine().Trim().Substring(1);

                // get current synth setting
                curSynth = double.Parse(readBuffer);
                curSynth /= 100000;
                curSynth += 5700;

            }

            void setSynth()
            {
                if (!efos.IsOpen)
                    efos.Open();

                sendBuffer = ((Double)((newSynth - 5700) * 1e5)).ToString("0000000");
                efos.Write(sendBuffer);
                readBuffer = efos.ReadLine();

                //log.WriteLine("{0} {1} -> {2} ({3}) (sendbuffer: {4}, readbuffer: {5})", DateTime.Now, curSynth.ToString("0000.00000"), newSynth.ToString("0000.00000"), fDelta.ToString("+0.000E+00;-0.000E+00"), sendBuffer.Trim(), readBuffer.Trim());
                log.WriteLine("{0} {1} -> {2} ({3})", DateTime.UtcNow, curSynth.ToString("0000.00000"), newSynth.ToString("0000.00000"), fDelta.ToString("+0.000E+00;-0.000E+00"));
                log.Flush();
            }

            getCurrentSynth();
            newSynth = curSynth;

            // If given an argument, interpret as counts of least possible steps. Set synthesizer and return
            if(args.Length == 1)
            {
                int count = int.Parse(args[0]);
                newSynth += count*1e-5;
                ShowFrqOffset();
                setSynth();

                return;
            }

            ShowPrompt();

            while (true) {
                
                ConsoleKeyInfo c = Console.ReadKey(true);
                switch (c.Key) {
                    case ConsoleKey.UpArrow:
                        newSynth += multiplier;

                        ShowPrompt();

                        break;

                    case ConsoleKey.DownArrow:
                        newSynth -= multiplier;

                        ShowPrompt();

                        break;

                    case ConsoleKey.LeftArrow:
                        curPos--;
                        if (curPos < minCurPos)
                            curPos = minCurPos;
                        else
                        {
                            if (curPos == dotPos)
                                curPos--;

                            multiplier *= 10;
                        }

                        Console.SetCursorPosition(curPos, 1);

                        break;

                    case ConsoleKey.RightArrow:
                        curPos++;
                        if (curPos > maxCurPos)
                            curPos = maxCurPos;
                        else
                        {
                            if (curPos == dotPos)
                                curPos++;

                            multiplier /= 10;
                        }

                        Console.SetCursorPosition(curPos, 1);

                        break;

                    case ConsoleKey.Enter:

                        setSynth();
                        getCurrentSynth();
                        newSynth = curSynth;
                        ShowPrompt();

                        break;
                }

                efos.Close();
            }
        }
    }
}
