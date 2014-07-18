using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SQLite;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace StationNamer
{
    class Program
    {
        private static Stationer _stationer;

        private static Stationer Stationer
        {
            get
            {
                if (_stationer == null)
                    _stationer = new Stationer();
                return _stationer;
            }
        }

        private static void Main(string[] args)
        {
            if (args.Length > 0 && Process(args))
                return;
            do
            {
                Console.Write("Command: ");
                var line = Console.ReadLine();
                if (String.IsNullOrWhiteSpace(line))
                    continue;
                if (Process(line.Split(' ')))
                    return;
            } while (true);
        }

        private static bool Process(IList<string> args)
        {
            try
            {
                switch (args[0])
                {
                    case "info":
                        WriteInfo(Stationer.StationsToInsert, "Insert");
                        WriteInfo(Stationer.StationsToDelete, "Delete");
                        WriteInfo(Stationer.StationsToUpdate, "Update");
                        break;
                    case "sync":
                        Stationer.Sync();
                        break;
                    case "update":
                        Stationer.Update();
                        break;
                    case "insert":
                        Stationer.Insert();
                        break;
                    case "delete":
                        Stationer.Delete();
                        break;
                    case "exit":
                    case "quit":
                        return true;
                    case "list":
                        if (args.Count >= 2 && args[1] == "wiki")
                            WriteInfo(Stationer.WikiStations);
                        else
                            WriteInfo(Stationer.Stations);
                        break;
                    case "fav":
                        Stationer.MarkAllFavourite();
                        break;
                    case "unfav":
                        Stationer.UnmarkAllFavourite();
                        break;
                    default:
                        Console.WriteLine("-- unknown");
                        break;

                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
            return false;
        }

        private static void WriteInfo(IEnumerable<Stationer.Station> stations, string title = null)
        {
            if (!stations.Any())
                return;
            if (title != null)
                Console.WriteLine(title);
            foreach (var station in stations)
                Console.WriteLine("{0,5:#.0} - {1}", station.Frequency, station.Name);
        }
    }
}
