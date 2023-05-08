﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GPTStudio.TelegramProvider.Utils;
internal static class Logger
{
    public static void Print(string value,bool endl = true, ConsoleColor color = ConsoleColor.White)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write(DateTime.Now + "\t| ");
        Console.ForegroundColor = color;
        Console.Write(value + (endl ? "\n" : ""));
        Console.ForegroundColor = ConsoleColor.White;
    }

    public static void PrintError(string value)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write(DateTime.Now + "\t| ");
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(value);
        Console.ForegroundColor = ConsoleColor.White;
    }
}