using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading;
using Domain;

namespace Client
{
    public class Client
    {
        private const int UdpPort = 15001;
        private const int TcpPort = 15002;
        private const string ServerIp = "127.0.0.1";

        private static Socket _tcpSocket;
        private static int _flightId;
        private static Let _currentFlight;
        private static readonly object _lockObject = new object();
        private static bool _flightActive = false;

        private static void Main(string[] args)
        {
            Console.WriteLine("=== AVION - KLIJENT ===");
            Console.WriteLine("Pokretanje aviona...\n");

            try
            {
                while (true)
                {
                    Console.WriteLine("\n--- GLAVNI MENI ---");
                    Console.WriteLine("1. Zahtev za ulazak u vazdusni prostor");
                    Console.WriteLine("2. Izlaz");
                    Console.Write("Izaberite opciju: ");

                    var choice = Console.ReadLine();

                    switch (choice)
                    {
                        case "1":
                            RequestFlightEntry();
                            break;
                        case "2":
                            Console.WriteLine("Gasenje klijenta...");
                            return;
                        default:
                            Console.WriteLine("Nevalidna opcija.");
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Greska: {ex.Message}");
            }
            finally
            {
                if (_tcpSocket != null && _tcpSocket.Connected)
                {
                    _tcpSocket.Close();
                }
            }
        }

        private static void RequestFlightEntry()
        {
            try
            {
                Console.WriteLine("\n=== KREIRANJE ZAHTEVA ZA LET ===\n");

                // Kreiranje letelice
                var letelica = new Letelica();
                Console.Write("Naziv aviokompanije: ");
                letelica.NazivAviokompanije = Console.ReadLine();

                Console.Write("Naziv glavnog pilota: ");
                letelica.NazivGlavnogPilota = Console.ReadLine();

                Console.Write("Registracija aviona: ");
                letelica.RegistracijaAviona = Console.ReadLine();

                Console.Write("Maksimalan broj putnika: ");
                letelica.MaksimalanBrojPutnika = int.Parse(Console.ReadLine() ?? "0");

                Console.Write("Trenutni broj putnika: ");
                letelica.TrenutanBrojPutnika = int.Parse(Console.ReadLine() ?? "0");

                // Kreiranje leta
                var let = new Let { Letelica = letelica };

                Console.WriteLine("\n--- Pocetne koordinate ---");
                Console.Write("Pocetna X koordinata: ");
                let.PocetnaX = int.Parse(Console.ReadLine() ?? "0");

                Console.Write("Pocetna Y koordinata: ");
                let.PocetnaY = int.Parse(Console.ReadLine() ?? "0");

                Console.Write("Pocetna Z koordinata (1-3): ");
                let.PocetnaZ = int.Parse(Console.ReadLine() ?? "1");

                Console.WriteLine("\n--- Krajnje koordinate ---");
                Console.Write("Krajnja X koordinata: ");
                let.KrajnjaX = int.Parse(Console.ReadLine() ?? "0");

                Console.Write("Krajnja Y koordinata: ");
                let.KrajnjaY = int.Parse(Console.ReadLine() ?? "0");

                Console.Write("Krajnja Z koordinata (1-3): ");
                let.KrajnjaZ = int.Parse(Console.ReadLine() ?? "1");

                // Postavi trenutnu poziciju na pocetnu
                let.TrenutnaX = let.PocetnaX;
                let.TrenutnaY = let.PocetnaY;
                let.TrenutnaZ = let.PocetnaZ;

                // Kreiraj zahtev i posalji preko UDP-a
                var zahtev = new ZahtevZaUlazak { Let = let };

                Console.WriteLine("\nSlanje zahteva za ulazak u vazdusni prostor...");
                var odgovor = SendUdpRequest(zahtev);

                if (odgovor == null)
                {
                    Console.WriteLine("Server nije odgovorio. Pokusajte ponovo.");
                    return;
                }

                Console.WriteLine($"\nOdgovor servera: {odgovor}");

                if (odgovor.Odobren)
                {
                    // Zahtev odobren
                    _flightId = odgovor.RequestId;
                    _currentFlight = let;
                    Console.WriteLine($"Let odobren! ID leta: {_flightId}");
                    Console.WriteLine($"Poruka: {odgovor.Poruka}");

                    // Uspostavi TCP konekciju za kontinuiranu komunikaciju
                    Console.WriteLine("\nUspostavljanje TCP konekcije...");
                    if (EstablishTcpConnection())
                    {
                        Console.WriteLine("Potvrda o pocetku leta primljena. Let zapocinje!");
                        StartFlight();
                    }
                    else
                    {
                        Console.WriteLine("Neuspesna TCP konekcija. Let otkazan.");
                    }
                }
                else
                {
                    // Zahtev odbijen - proveri da li postoji korekcija koordinata
                    Console.WriteLine($"Zahtev odbijen: {odgovor.Poruka}");

                    if (odgovor.NovaX != -1 && odgovor.NovaY != -1 && odgovor.NovaZ != -1)
                    {
                        Console.WriteLine($"\nServer predlaze korekciju koordinata: ({odgovor.NovaX},{odgovor.NovaY},{odgovor.NovaZ})");
                        Console.Write("Da li zelite da prihvatite korekciju? (da/ne): ");
                        var response = Console.ReadLine()?.ToLower();

                        if (response == "da" || response == "d")
                        {
                            let.PocetnaX = odgovor.NovaX;
                            let.PocetnaY = odgovor.NovaY;
                            let.PocetnaZ = odgovor.NovaZ;
                            let.TrenutnaX = let.PocetnaX;
                            let.TrenutnaY = let.PocetnaY;
                            let.TrenutnaZ = let.PocetnaZ;

                            zahtev.Let = let;
                            Console.WriteLine("\nSlanje korigovanog zahteva...");
                            var noviOdgovor = SendUdpRequest(zahtev);

                            if (noviOdgovor != null && noviOdgovor.Odobren)
                            {
                                _flightId = noviOdgovor.RequestId;
                                _currentFlight = let;
                                Console.WriteLine($"Let odobren sa korekcijom! ID leta: {_flightId}");

                                Console.WriteLine("\nUspostavljanje TCP konekcije...");
                                if (EstablishTcpConnection())
                                {
                                    Console.WriteLine("Potvrda o pocetku leta primljena. Let zapocinje!");
                                    StartFlight();
                                }
                            }
                            else
                            {
                                Console.WriteLine("Korigovani zahtev takodje odbijen.");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Greska pri zahtevanju leta: {ex.Message}");
            }
        }

        #region UDP komunikacija

        private static OdgovorServera SendUdpRequest(ZahtevZaUlazak zahtev)
        {
            using (var udpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                try
                {
                    udpSocket.ReceiveTimeout = 5000; // 5 sekundi timeout

                    var serverEndPoint = new IPEndPoint(IPAddress.Parse(ServerIp), UdpPort);
                    var requestData = SerializeObject(zahtev);
                    udpSocket.SendTo(requestData, serverEndPoint);

                    Console.WriteLine("[UDP] Zahtev poslat serveru, cekanje odgovora...");

                    var buffer = new byte[8192];
                    EndPoint remoteEp = new IPEndPoint(IPAddress.Any, 0);
                    var received = udpSocket.ReceiveFrom(buffer, ref remoteEp);

                    if (received > 0)
                    {
                        return DeserializeObject<OdgovorServera>(buffer, received);
                    }
                }
                catch (SocketException ex)
                {
                    Console.WriteLine($"[UDP] Greska: {ex.Message}");
                }

                return null;
            }
        }

        #endregion

        #region TCP komunikacija

        private static bool EstablishTcpConnection()
        {
            try
            {
                _tcpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                _tcpSocket.Connect(IPAddress.Parse(ServerIp), TcpPort);

                // Posalji flight ID za identifikaciju
                var flightIdData = Encoding.UTF8.GetBytes(_flightId.ToString());
                _tcpSocket.Send(flightIdData);

                // Cekaj potvrdu od servera
                var buffer = new byte[1024];
                var received = _tcpSocket.Receive(buffer);
                var response = Encoding.UTF8.GetString(buffer, 0, received);

                if (response == "TCP_CONNECTED")
                {
                    Console.WriteLine("[TCP] Konekcija uspesno uspostavljena.");
                    return true;
                }
                else
                {
                    Console.WriteLine($"[TCP] Server odbio konekciju: {response}");
                    _tcpSocket.Close();
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TCP] Greska pri konekciji: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Let

        private static void StartFlight()
        {
            _flightActive = true;
            _currentFlight.VremePocetka = DateTime.Now;

            Console.WriteLine($"\n=== LET ZAPOCINJE ===");
            Console.WriteLine($"Avion: {_currentFlight.Letelica.RegistracijaAviona}");
            Console.WriteLine($"Ruta: ({_currentFlight.PocetnaX},{_currentFlight.PocetnaY},{_currentFlight.PocetnaZ}) -> " +
                $"({_currentFlight.KrajnjaX},{_currentFlight.KrajnjaY},{_currentFlight.KrajnjaZ})");
            Console.WriteLine("Pritisnite bilo koji taster za pocetak kretanja...");
            Console.ReadKey(true);

            // Pokreni thread za prijem TCP poruka od servera
            var receiveThread = new Thread(ReceiveTcpMessages) { IsBackground = true };
            receiveThread.Start();

            // Glavna petlja leta - kretanje aviona
            while (_flightActive)
            {
                try
                {
                    // Prikaz statusa leta
                    DisplayFlightStatus();

                    // Proveri da li smo stigli
                    if (HasReachedDestination())
                    {
                        CompleteFlight();
                        break;
                    }

                    // Izracunaj sledeci potez (jedan sektor u 3x3 okolini)
                    int nextX, nextY, nextZ;
                    CalculateNextMove(out nextX, out nextY, out nextZ);

                    // Posalji zahtev za pomeranje serveru
                    var message = $"POSITION_UPDATE:{nextX},{nextY},{nextZ}";
                    _tcpSocket.Send(Encoding.UTF8.GetBytes(message));

                    // Cekaj odgovor od servera (obradjen u ReceiveTcpMessages thread-u)
                    Thread.Sleep(3000);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"\nGreska tokom leta: {ex.Message}");
                    _flightActive = false;
                    break;
                }
            }
        }

        private static void ReceiveTcpMessages()
        {
            var buffer = new byte[4096];

            while (_flightActive)
            {
                try
                {
                    if (_tcpSocket.Poll(500000, SelectMode.SelectRead)) // 500ms
                    {
                        var received = _tcpSocket.Receive(buffer);
                        if (received == 0)
                        {
                            Console.WriteLine("\n[TCP] Server zatvorio konekciju.");
                            _flightActive = false;
                            break;
                        }

                        var message = Encoding.UTF8.GetString(buffer, 0, received);
                        ProcessServerMessage(message);
                    }
                }
                catch (SocketException)
                {
                    _flightActive = false;
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"\n[TCP] Greska pri prijemu: {ex.Message}");
                    break;
                }
            }
        }

        private static void ProcessServerMessage(string message)
        {
            if (message == "POSITION_OK")
            {
                lock (_lockObject)
                {
                    _currentFlight.BrojProdjeniSektora++;

                    // Posalji potvrdu prolaska kroz sektor
                    var sectorMsg = $"SECTOR_ENTERED:{_currentFlight.TrenutnaX},{_currentFlight.TrenutnaY},{_currentFlight.TrenutnaZ}";
                    try { _tcpSocket.Send(Encoding.UTF8.GetBytes(sectorMsg)); } catch { }
                }
            }
            else if (message.StartsWith("REDIRECT:"))
            {
                var parts = message.Split(':')[1].Split(',');
                if (parts.Length == 3 &&
                    int.TryParse(parts[0], out var newX) &&
                    int.TryParse(parts[1], out var newY) &&
                    int.TryParse(parts[2], out var newZ))
                {
                    lock (_lockObject)
                    {
                        _currentFlight.TrenutnaX = newX;
                        _currentFlight.TrenutnaY = newY;
                        _currentFlight.TrenutnaZ = newZ;
                        _currentFlight.BrojIzmenaputanje++;
                        _currentFlight.BrojProdjeniSektora++;
                    }

                    Console.WriteLine($"\n*** PREUSMERAVANJE: Nova pozicija ({newX},{newY},{newZ}) - Promena visine/putanje ***");
                }
            }
            else if (message == "DESTINATION_REACHED")
            {
                Console.WriteLine("\n*** SERVER POTVRDIO: ODREDISTE DOSTIGNUTO ***");
                _flightActive = false;
            }
        }

        private static void DisplayFlightStatus()
        {
            lock (_lockObject)
            {
                Console.Clear();
                Console.WriteLine("=============================================");
                Console.WriteLine("          STATUS LETA - KLIJENT");
                Console.WriteLine("=============================================");
                Console.WriteLine($"Avion: {_currentFlight.Letelica.RegistracijaAviona}");
                Console.WriteLine($"Aviokompanija: {_currentFlight.Letelica.NazivAviokompanije}");
                Console.WriteLine($"Pilot: {_currentFlight.Letelica.NazivGlavnogPilota}");
                Console.WriteLine($"Putnici: {_currentFlight.Letelica.TrenutanBrojPutnika}/{_currentFlight.Letelica.MaksimalanBrojPutnika}");
                Console.WriteLine("---------------------------------------------");
                Console.WriteLine($"Trenutne koordinate: ({_currentFlight.TrenutnaX},{_currentFlight.TrenutnaY},{_currentFlight.TrenutnaZ})");
                Console.WriteLine($"Odrediste:           ({_currentFlight.KrajnjaX},{_currentFlight.KrajnjaY},{_currentFlight.KrajnjaZ})");
                Console.WriteLine("---------------------------------------------");
                Console.WriteLine($"Broj predjenih sektora:       {_currentFlight.BrojProdjeniSektora}");
                Console.WriteLine($"Broj preostalih sektora:      {_currentFlight.BrojPreostalihSektora()}");
                Console.WriteLine($"Broj izmena putanje:          {_currentFlight.BrojIzmenaputanje}");
                Console.WriteLine($"Trajanje leta:                {DateTime.Now - _currentFlight.VremePocetka:mm\\:ss}");
                Console.WriteLine("=============================================");
            }
        }

        private static bool HasReachedDestination()
        {
            lock (_lockObject)
            {
                return _currentFlight.TrenutnaX == _currentFlight.KrajnjaX &&
                       _currentFlight.TrenutnaY == _currentFlight.KrajnjaY &&
                       _currentFlight.TrenutnaZ == _currentFlight.KrajnjaZ;
            }
        }

        private static void CalculateNextMove(out int nextX, out int nextY, out int nextZ)
        {
            lock (_lockObject)
            {
                nextX = _currentFlight.TrenutnaX;
                nextY = _currentFlight.TrenutnaY;
                nextZ = _currentFlight.TrenutnaZ;

                // Jedan korak u smeru cilja (prioritet: X -> Y -> Z)
                if (_currentFlight.TrenutnaX < _currentFlight.KrajnjaX)
                    nextX++;
                else if (_currentFlight.TrenutnaX > _currentFlight.KrajnjaX)
                    nextX--;
                else if (_currentFlight.TrenutnaY < _currentFlight.KrajnjaY)
                    nextY++;
                else if (_currentFlight.TrenutnaY > _currentFlight.KrajnjaY)
                    nextY--;
                else if (_currentFlight.TrenutnaZ < _currentFlight.KrajnjaZ)
                    nextZ++;
                else if (_currentFlight.TrenutnaZ > _currentFlight.KrajnjaZ)
                    nextZ--;

                // Azuriraj trenutnu poziciju (optimisticno - server moze korigovati)
                _currentFlight.TrenutnaX = nextX;
                _currentFlight.TrenutnaY = nextY;
                _currentFlight.TrenutnaZ = nextZ;
            }
        }

        private static void CompleteFlight()
        {
            try
            {
                // Obavesti server da je let zavrsen
                _tcpSocket.Send(Encoding.UTF8.GetBytes("FLIGHT_COMPLETED"));
                _flightActive = false;

                Console.Clear();
                Console.WriteLine("=============================================");
                Console.WriteLine("            LET ZAVRSEN USPESNO!");
                Console.WriteLine("=============================================");
                Console.WriteLine($"Avion: {_currentFlight.Letelica.RegistracijaAviona}");
                Console.WriteLine($"Aviokompanija: {_currentFlight.Letelica.NazivAviokompanije}");
                Console.WriteLine("---------------------------------------------");
                Console.WriteLine($"Ukupno predjenih sektora:  {_currentFlight.BrojProdjeniSektora}");
                Console.WriteLine($"Ukupno izmena putanje:     {_currentFlight.BrojIzmenaputanje}");
                Console.WriteLine($"Ukupno vreme leta:         {DateTime.Now - _currentFlight.VremePocetka:mm\\:ss}");
                Console.WriteLine("=============================================");

                Console.WriteLine("\nPritisnite bilo koji taster za povratak...");
                Console.ReadKey(true);

                _tcpSocket.Close();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Greska pri zavrsavanju leta: {ex.Message}");
            }
        }

        #endregion

        #region Serijalizacija

        private static byte[] SerializeObject(object obj)
        {
            using (var ms = new MemoryStream())
            {
                var bf = new BinaryFormatter();
                bf.Serialize(ms, obj);
                return ms.ToArray();
            }
        }

        private static T DeserializeObject<T>(byte[] data, int length)
        {
            using (var ms = new MemoryStream(data, 0, length))
            {
                var bf = new BinaryFormatter();
                return (T)bf.Deserialize(ms);
            }
        }

        #endregion
    }
}
