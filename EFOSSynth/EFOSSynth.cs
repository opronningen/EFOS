using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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

            SerialPort efos = new SerialPort(com, 9600);

            int curPos = 32;            // Cursor-position
            const int minCurPos = 25;   // "Left-most" valid position
            const int maxCurPos = 32;   // Right-most valid postion
            const int dotPos = 27;
            string readBuffer;
            string sendBuffer;

            char[] digits;

            int CursorPosToIndex(int cursorPos)
            {
                if (cursorPos >= dotPos)
                    return (cursorPos - minCurPos - 1);
                else
                    return (cursorPos - minCurPos);
            }

            void ShowPrompt()
            {
                Console.WriteLine("Current Synth setting: 57{0}.{1}", readBuffer.Substring(0, 2), readBuffer.Substring(2, 5));
                Console.WriteLine("New Synth setting:     57{0}.{1}", readBuffer.Substring(0, 2), readBuffer.Substring(2, 5));
                ShowFrqOffset();
            }

            const double f = 1420405751.00;
            double curSynth = 5751.689;
            double newSynth = curSynth;

            void ShowFrqOffset()
            {
                newSynth = double.Parse(new string(digits));
                newSynth /= 100000;
                newSynth += 5700;

                Console.SetCursorPosition(0, 2);
                Console.Write("Frequency offset:     {0} Hz       ", ((curSynth - newSynth) / f).ToString(" 0.000E+00;-0.000E+00"));
                Console.SetCursorPosition(curPos, 1);
            }

            void cursorLeft()
            {
                curPos--;
                if (curPos < minCurPos)
                    curPos = minCurPos;
                else if (curPos == dotPos)
                    curPos--;

                Console.SetCursorPosition(curPos, 1);
            }

            void cursorRight()
            {
                curPos++;
                if (curPos > maxCurPos)
                    curPos = maxCurPos;
                else if (curPos == dotPos)
                    curPos++;

                Console.SetCursorPosition(curPos, 1);
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

                digits = readBuffer.ToCharArray();
            }

            int index;
            char digit;

            getCurrentSynth();
			
			Console.Clear();
            ShowPrompt();
            Console.SetCursorPosition(curPos, 1);

            while (true) {
                
                ConsoleKeyInfo c = Console.ReadKey(true);
                switch (c.Key) {
                    case ConsoleKey.UpArrow:
                        index = CursorPosToIndex(curPos);
                        digit = digits[index];
                        digit++;
                        if (digit > '9')
                            digit = '0';

                        digits[index] = digit;

                        Console.Write(digit);
                        ShowFrqOffset();
                        break;

                    case ConsoleKey.DownArrow:
                        index = CursorPosToIndex(curPos);
                        digit = digits[index];
                        digit--;
                        if (digit < '0')
                            digit = '9';

                        digits[index] = digit;

                        Console.Write(digit);
                        ShowFrqOffset();
                        break;

                    case ConsoleKey.LeftArrow:
                        cursorLeft();
                        break;

                    case ConsoleKey.RightArrow:
                        cursorRight();
                        break;

                    case ConsoleKey.Enter:
                        sendBuffer = new string(digits);

                        if (!efos.IsOpen)
                            efos.Open();

                        efos.Write(sendBuffer);
                        readBuffer = efos.ReadLine();

                        getCurrentSynth();

                        Console.Clear();
                        ShowPrompt();
                        Console.SetCursorPosition(curPos, 1);

                        break;

                    default:
                        char chr = c.KeyChar;
                        if (chr >= '0' && chr <= '9') {
                            Console.Write(chr);
                            digits[CursorPosToIndex(curPos)] = chr;
                            cursorRight();
                            ShowFrqOffset();
                        }

                        break;
                }

                efos.Close();
            }
        }
    }
}
