using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SQLite;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using HtmlAgilityPack;

namespace StationNamer
{
    public class Stationer
    {
        public class Station : IEquatable<Station>
        {
            public decimal Frequency { set; get; }
            public string Name { set; get; }
            public string Url { set; get; }
            public bool HasRds { set; get; }
            public decimal Capacity { set; get; }
            public string Tower { set; get; }

            public bool Equals(Station other)
            {
                return Frequency == other.Frequency;
            }

            public override int GetHashCode()
            {
                return Frequency.GetHashCode();
            }

            public override bool Equals(object obj)
            {
                if (!(obj is Station))
                    return false;
                return Equals((Station)obj);
            }
        }

        public class StationFrequenceNameComparer : IEqualityComparer<Station>
        {
            private static Dictionary<int, StationFrequenceNameComparer> _instances;
            public static StationFrequenceNameComparer GetInstance(int maxNameLength)
            {
                StationFrequenceNameComparer result;
                if (_instances == null)
                    _instances = new Dictionary<int, StationFrequenceNameComparer>(1);
                else if (_instances.TryGetValue(maxNameLength, out result))
                    return result;
                result = new StationFrequenceNameComparer { MaxNameLength = maxNameLength };
                _instances.Add(maxNameLength, result);
                return result;
            }

            public int MaxNameLength { set; get; }

            private StationFrequenceNameComparer()
            {
            }

            public bool Equals(Station x, Station y)
            {
                return x.Frequency == y.Frequency && Utils.Left(x.Name, MaxNameLength) == Utils.Left(y.Name, MaxNameLength);
            }

            public int GetHashCode(Station obj)
            {
                return obj.Frequency.GetHashCode() ^ obj.Name.GetHashCode();
            }
        }

        private Station[] _wikiStations;
        public Station[] WikiStations
        {
            get
            {
                if (_wikiStations == null)
                {
                    var request = WebRequest.Create(ConfigurationManager.AppSettings["SourceURL"]);
                    request.Proxy.Credentials = CredentialCache.DefaultCredentials;
                    var response = request.GetResponse();
                    var doc = new HtmlDocument { OptionFixNestedTags = true };
                    doc.Load(response.GetResponseStream());
                    var stations = doc.DocumentNode
                        .SelectNodes("descendant::table[@class='standard sortable'][1]/tr[position()>1]")
                        .Select(@node => new Station
                        {
                            Frequency = Decimal.Parse(@node.SelectSingleNode("td[1]").InnerText.Replace(",", "."), CultureInfo.InvariantCulture),
                            Name = Utils.Decode(@node.SelectSingleNode("td[2]/a[1]").InnerText),
                            Url = @node.SelectSingleNode("td[3]/a[1]/@href").InnerText,
                            HasRds = @node.SelectSingleNode("td[4]").InnerText == "+",
                            Capacity = Decimal.Parse(@node.SelectSingleNode("td[5]").InnerText.Replace(",", "."), CultureInfo.InvariantCulture),
                            Tower = @node.SelectSingleNode("td[6]").InnerText
                        });
                    _wikiStations = FmOnly(stations).ToArray();
                }
                return _wikiStations;
            }
        }

        private IEnumerable<Station> FmOnly(IEnumerable<Station> stations)
        {
            return stations.Where(@station => @station.Frequency >= 87.5m && @station.Frequency <= 108m);
        }

        private SQLiteConnection _connection;
        private SQLiteConnection Connection
        {
            get
            {
                if (_connection == null)
                {
                    _connection = new SQLiteConnection(ConfigurationManager.ConnectionStrings["Radio"].ConnectionString);
                    _connection.Open();
                }
                return _connection;
            }
        }

        private List<Station> _stations;
        private SQLiteCommand _getStationsCommand;
        public List<Station> Stations
        {
            get
            {
                if (_stations == null)
                {
                    _stations = new List<Station>();
                    if (_getStationsCommand == null)
                    {
                        _getStationsCommand = Connection.CreateCommand();
                        _getStationsCommand.CommandText = "SELECT column_station_freq, column_station_name FROM StationList WHERE column_station_type in (2,3) ORDER BY column_station_freq";
                    }
                    using (var reader = _getStationsCommand.ExecuteReader())
                        while (reader.Read())
                            _stations.Add(new Station
                            {
                                Frequency = reader.GetInt32(0) / 10m,
                                Name = reader.GetString(1)
                            });
                }
                return _stations;
            }
        }

        public IEnumerable<Station> StationsToInsert
        {
            get
            {
                return WikiStations.Except(Stations);
            }
        }
        private SQLiteCommand _insertCommand;
        private void InnerInsert(bool final = true)
        {
            var stationsToInsert = StationsToInsert.ToArray();
            if (stationsToInsert.Length == 0)
                return;
            if (_insertCommand == null)
            {
                _insertCommand = Connection.CreateCommand();
                _insertCommand.CommandText = "INSERT INTO StationList(column_station_name,column_station_freq,column_station_type) VALUES(?,?,3)";

                var param = _insertCommand.CreateParameter();
                param.DbType = DbType.String;
                param.Size = 15;
                _insertCommand.Parameters.Add(param);

                param = _insertCommand.CreateParameter();
                param.DbType = DbType.Int32;
                _insertCommand.Parameters.Add(param);
            }
            foreach (var wikiStation in stationsToInsert)
            {
                _insertCommand.Parameters[0].Value = Utils.Left(wikiStation.Name, 15);
                _insertCommand.Parameters[1].Value = (int)(wikiStation.Frequency * 10);
                _insertCommand.ExecuteNonQuery();
            }
            if (final)
                _stations = null;
        }
        public void Insert()
        {
            InnerInsert();
        }

        public IEnumerable<Station> StationsToUpdate
        {
            get
            {
                return WikiStations.Intersect(Stations).Except(WikiStations, StationFrequenceNameComparer.GetInstance(15));
            }
        }
        private SQLiteCommand _updateCommand;
        private void InnerUpdate(bool final = true)
        {
            var stationsToUpdate = StationsToUpdate.ToArray();
            if (stationsToUpdate.Length == 0)
                return;
            if (_updateCommand == null)
            {
                _updateCommand = Connection.CreateCommand();
                _updateCommand.CommandText = ConfigurationManager.AppSettings["UpdateStmt"];

                var param = _updateCommand.CreateParameter();
                param.DbType = DbType.String;
                param.Size = 15;
                _updateCommand.Parameters.Add(param);

                param = _updateCommand.CreateParameter();
                param.DbType = DbType.Int32;
                _updateCommand.Parameters.Add(param);
            }
            foreach (var wikiStation in stationsToUpdate)
            {
                _updateCommand.Parameters[0].Value = Utils.Left(wikiStation.Name, 15);
                _updateCommand.Parameters[1].Value = (int)(wikiStation.Frequency * 10);
                _updateCommand.ExecuteNonQuery();
            }
            if (final)
                _stations = null;
        }
        public void Update()
        {
            InnerUpdate();
        }

        public IEnumerable<Station> StationsToDelete
        {
            get
            {
                return Stations.Except(WikiStations);
            }
        }
        private SQLiteCommand _deleteCommand;
        private void InnerDelete(bool final = true)
        {
            var stationsToDelete = StationsToDelete.ToArray();
            if (stationsToDelete.Length == 0)
                return;
            if (_deleteCommand == null)
            {
                _deleteCommand = Connection.CreateCommand();
                _deleteCommand.CommandText = "DELETE FROM StationList WHERE column_station_freq = ?";

                var param = _deleteCommand.CreateParameter();
                param.DbType = DbType.Int32;
                _deleteCommand.Parameters.Add(param);
            }
            foreach (var stationToDelete in stationsToDelete)
            {
                _deleteCommand.Parameters[0].Value = (int)(stationToDelete.Frequency * 10);
                _deleteCommand.ExecuteNonQuery();
            }
            if (final)
                _stations = null;
        }
        public void Delete()
        {
            InnerDelete();
        }

        public void Sync()
        {
            InnerUpdate(false);
            InnerDelete(false);
            Insert();
        }

        private SQLiteCommand _favCommand;
        private void InnerFavouritize(bool favourite)
        {
            if (_favCommand == null)
            {
                _favCommand = Connection.CreateCommand();
                _favCommand.CommandText = "UPDATE StationList SET column_station_type = ? WHERE column_station_type = ?";


                var param = _favCommand.CreateParameter();
                param.DbType = DbType.Int32;
                _favCommand.Parameters.Add(param);

                param = _favCommand.CreateParameter();
                param.DbType = DbType.Int32;
                _favCommand.Parameters.Add(param);
            }
            if (favourite)
            {
                _favCommand.Parameters[0].Value = 2;
                _favCommand.Parameters[1].Value = 3;
            }
            else
            {
                _favCommand.Parameters[0].Value = 3;
                _favCommand.Parameters[1].Value = 2;
            }
            _favCommand.ExecuteNonQuery();
        }
        public void MarkAllFavourite()
        {
            InnerFavouritize(true);
        }
        public void UnmarkAllFavourite()
        {
            InnerFavouritize(false);
        }
    }
}
