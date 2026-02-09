using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading;
using Domain;

namespace Server
{
    public class Server
    {
        private const int UdpPort = 15001;
        private const int TcpPort = 15002;

        private static VazdusniProstor _vazdusniProstor;
        private static readonly Dictionary<int, Let> _aktivniLetovi = new Dictionary<int, Let>();
        private static readonly Dictionary<int, Socket> _avionSockets = new Dictionary<int, Socket>();
        private static readonly List<ZahtevZaUlazak> _zahteviZaUlazak = new List<ZahtevZaUlazak>();
        private static int _nextFlightId = 1;
        private static readonly object _lockObject = new object();
        private static DateTime _lastDisplayTime = DateTime.MinValue;

        private static void Main(string[] args)
        {
            Console.WriteLine("=== SISTEM KONTROLE LETA - SERVER ===");
            Console.WriteLine("Pokretanje servera...\n");

            InitializeAirspace();

            // Kreiranje UDP soketa
            var udpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            udpSocket.Bind(new IPEndPoint(IPAddress.Any, UdpPort));

            // Kreiranje TCP soketa
            var tcpListener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            tcpListener.Bind(new IPEndPoint(IPAddress.Any, TcpPort));
            tcpListener.Listen(10);

            Console.WriteLine($"Server slusa na UDP portu {UdpPort} i TCP portu {TcpPort}");
            Console.WriteLine("Vazdusni prostor je inicijalizovan i spreman za rad.\n");

            // Pokretanje thread-a za periodicno azuriranje letova
            var updateThread = new Thread(UpdateFlightsLoop) { IsBackground = true };
            updateThread.Start();

            // Glavna petlja - multipleksiranje
            while (true)
            {
                try
                {
                    // 1. Proveri UDP zahteve za ulazak
                    if (udpSocket.Poll(100000, SelectMode.SelectRead)) // 100ms
                    {
                        HandleUdpRequest(udpSocket);
                    }

                    // 2. Proveri nove TCP konekcije
                    if (tcpListener.Poll(100000, SelectMode.SelectRead))
                    {
                        var clientSocket = tcpListener.Accept();
                        Console.WriteLine($"\nNova TCP konekcija od {clientSocket.RemoteEndPoint}");
                        HandleNewTcpConnection(clientSocket);
                    }

                    // 3. Proveri poruke od postojecih TCP klijenata
                    ProcessTcpClients();

                    // 4. Prikaz statusa
                    DisplayStatus();

                    Thread.Sleep(50);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Greska u glavnoj petlji: {ex.Message}");
                }
            }
        }

        private static void InitializeAirspace()
        {
            Console.Write("Unesite sirinu vazdusnog prostora (broj sektora X): ");
            var sirina = int.Parse(Console.ReadLine() ?? "10");

            Console.Write("Unesite visinu vazdusnog prostora (broj sektora Y): ");
            var visina = int.Parse(Console.ReadLine() ?? "10");

            _vazdusniProstor = new VazdusniProstor(sirina, visina);

            Console.WriteLine($"\nVazdusni prostor kreiran: {sirina}x{visina} sa {_vazdusniProstor.NivoiVisine} nivoa visine (Z=1,2,3)");
            Console.WriteLine($"Ukupno sektora: {sirina * visina * _vazdusniProstor.NivoiVisine}");
        }

        #region UDP - Zahtevi za ulazak

        private static void HandleUdpRequest(Socket udpSocket)
        {
            var buffer = new byte[8192];
            EndPoint remoteEp = new IPEndPoint(IPAddress.Any, 0);

            var received = udpSocket.ReceiveFrom(buffer, ref remoteEp);
            if (received <= 0) return;

            try
            {
                var zahtev = DeserializeObject<ZahtevZaUlazak>(buffer, received);
                Console.WriteLine($"\n[UDP] Primljen zahtev od {remoteEp}: {zahtev.Let?.Letelica?.RegistracijaAviona}");

                lock (_lockObject)
                {
                    _zahteviZaUlazak.Add(zahtev);
                }

                var odgovor = ProcessFlightRequest(zahtev);
                var responseData = SerializeObject(odgovor);
                udpSocket.SendTo(responseData, remoteEp);

                Console.WriteLine($"[UDP] Poslat odgovor: {odgovor}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UDP] Greska pri obradi zahteva: {ex.Message}");
            }
        }

        private static OdgovorServera ProcessFlightRequest(ZahtevZaUlazak zahtev)
        {
            var let = zahtev.Let;

            // Proveri validnost koordinata
            if (!_vazdusniProstor.IsSektorValid(let.PocetnaX, let.PocetnaY, let.PocetnaZ) ||
                !_vazdusniProstor.IsSektorValid(let.KrajnjaX, let.KrajnjaY, let.KrajnjaZ))
            {
                zahtev.StatusZahteva = "Odbijen";
                return new OdgovorServera
                {
                    Odobren = false,
                    Poruka = "Nevalidne koordinate pocetka ili kraja leta"
                };
            }

            // Proveri pocetni sektor
            var pocetniSektor = _vazdusniProstor.GetSektor(let.PocetnaX, let.PocetnaY, let.PocetnaZ);

            if (pocetniSektor.Zauzet)
            {
                // Pokusaj alternativnu visinu
                var alternativa = FindAlternativeAltitude(let.PocetnaX, let.PocetnaY, let.PocetnaZ);
                if (alternativa != null)
                {
                    zahtev.StatusZahteva = "Korekcija";
                    return new OdgovorServera
                    {
                        Odobren = false,
                        Poruka = "Pocetni sektor zauzet - predlog korekcije",
                        NovaX = alternativa.X,
                        NovaY = alternativa.Y,
                        NovaZ = alternativa.Z
                    };
                }

                zahtev.StatusZahteva = "Odbijen";
                return new OdgovorServera
                {
                    Odobren = false,
                    Poruka = "Pocetni sektor zauzet, nema slobodnih alternativa"
                };
            }

            if (pocetniSektor.AbnormalniVremenskiUslovi)
            {
                // Pokusaj alternativnu visinu
                var alternativa = FindAlternativeAltitude(let.PocetnaX, let.PocetnaY, let.PocetnaZ);
                if (alternativa != null)
                {
                    zahtev.StatusZahteva = "Korekcija";
                    return new OdgovorServera
                    {
                        Odobren = false,
                        Poruka = "Abnormalni vremenski uslovi - predlog korekcije",
                        NovaX = alternativa.X,
                        NovaY = alternativa.Y,
                        NovaZ = alternativa.Z
                    };
                }

                zahtev.StatusZahteva = "Odbijen";
                return new OdgovorServera
                {
                    Odobren = false,
                    Poruka = "Abnormalni vremenski uslovi, nema slobodnih alternativa"
                };
            }

            // Proveri putanju - da li postoje problematicni sektori na putu
            bool putanjaProblematicna = false;
            string putanjaPoruka = "";
            CheckPath(let, ref putanjaProblematicna, ref putanjaPoruka);

            // Odobri let
            int flightId;
            lock (_lockObject)
            {
                flightId = _nextFlightId++;
                let.TrenutnaX = let.PocetnaX;
                let.TrenutnaY = let.PocetnaY;
                let.TrenutnaZ = let.PocetnaZ;

                _aktivniLetovi[flightId] = let;
                pocetniSektor.Zauzet = true;
                pocetniSektor.ZauzetOdAviona = let.Letelica.RegistracijaAviona;
                pocetniSektor.ZauzetOdLetaId = flightId;
            }

            zahtev.StatusZahteva = "Odobren";

            var poruka = "Let odobren. Potvrda o pocetku leta.";
            if (putanjaProblematicna)
                poruka += $" Napomena: {putanjaPoruka}";

            return new OdgovorServera
            {
                Odobren = true,
                Poruka = poruka,
                RequestId = flightId
            };
        }

        private static void CheckPath(Let let, ref bool problematicna, ref string poruka)
        {
            int x = let.PocetnaX, y = let.PocetnaY, z = let.PocetnaZ;
            int zauzeti = 0, abnormalni = 0;

            while (x != let.KrajnjaX || y != let.KrajnjaY)
            {
                if (x < let.KrajnjaX) x++;
                else if (x > let.KrajnjaX) x--;
                else if (y < let.KrajnjaY) y++;
                else if (y > let.KrajnjaY) y--;

                var s = _vazdusniProstor.GetSektor(x, y, z);
                if (s != null)
                {
                    if (s.Zauzet) zauzeti++;
                    if (s.AbnormalniVremenskiUslovi) abnormalni++;
                }
            }

            if (zauzeti > 0 || abnormalni > 0)
            {
                problematicna = true;
                poruka = $"Na putanji: {zauzeti} zauzetih i {abnormalni} sektora sa losim vremenom - moguce korekcije u toku leta.";
            }
        }

        #endregion

        #region TCP - Povezivanje i komunikacija

        private static void HandleNewTcpConnection(Socket clientSocket)
        {
            try
            {
                var buffer = new byte[1024];
                var received = clientSocket.Receive(buffer);

                if (received > 0)
                {
                    var message = Encoding.UTF8.GetString(buffer, 0, received);
                    if (int.TryParse(message, out var flightId))
                    {
                        lock (_lockObject)
                        {
                            if (_aktivniLetovi.ContainsKey(flightId))
                            {
                                _avionSockets[flightId] = clientSocket;
                                Console.WriteLine($"[TCP] Avion {flightId} ({_aktivniLetovi[flightId].Letelica.RegistracijaAviona}) povezan");

                                // Posalji potvrdu o pocetku leta
                                var response = Encoding.UTF8.GetBytes("TCP_CONNECTED");
                                clientSocket.Send(response);
                            }
                            else
                            {
                                Console.WriteLine($"[TCP] Nepoznat flight ID: {flightId}");
                                clientSocket.Send(Encoding.UTF8.GetBytes("REJECTED"));
                                clientSocket.Close();
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TCP] Greska pri povezivanju: {ex.Message}");
                clientSocket.Close();
            }
        }

        private static void ProcessTcpClients()
        {
            var socketsToRemove = new List<int>();

            lock (_lockObject)
            {
                foreach (var kvp in _avionSockets.ToList())
                {
                    try
                    {
                        if (kvp.Value.Poll(1000, SelectMode.SelectRead))
                        {
                            if (!HandleTcpMessage(kvp.Key, kvp.Value))
                            {
                                socketsToRemove.Add(kvp.Key);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[TCP] Greska sa avionom {kvp.Key}: {ex.Message}");
                        socketsToRemove.Add(kvp.Key);
                    }
                }

                foreach (var flightId in socketsToRemove)
                {
                    RemoveFlight(flightId);
                }
            }
        }

        private static bool HandleTcpMessage(int flightId, Socket clientSocket)
        {
            try
            {
                var buffer = new byte[4096];
                var received = clientSocket.Receive(buffer);

                if (received == 0) return false;

                var message = Encoding.UTF8.GetString(buffer, 0, received);

                if (message.StartsWith("POSITION_UPDATE:"))
                {
                    HandlePositionUpdate(flightId, message, clientSocket);
                }
                else if (message.StartsWith("SECTOR_ENTERED:"))
                {
                    HandleSectorEntered(flightId, message);
                }
                else if (message == "FLIGHT_COMPLETED")
                {
                    HandleFlightCompleted(flightId);
                    return false;
                }

                return true;
            }
            catch (SocketException)
            {
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TCP] Greska pri obradi poruke od aviona {flightId}: {ex.Message}");
                return false;
            }
        }

        private static void HandlePositionUpdate(int flightId, string message, Socket clientSocket)
        {
            var parts = message.Split(':')[1].Split(',');
            if (parts.Length != 3 ||
                !int.TryParse(parts[0], out var x) ||
                !int.TryParse(parts[1], out var y) ||
                !int.TryParse(parts[2], out var z))
                return;

            if (!_aktivniLetovi.TryGetValue(flightId, out var let)) return;

            // Proveri da li je avion stigao na odrediste
            if (x == let.KrajnjaX && y == let.KrajnjaY && z == let.KrajnjaZ)
            {
                // Oslobodi stari sektor
                OslobodiSektor(let.TrenutnaX, let.TrenutnaY, let.TrenutnaZ);

                // Zauzmi novi (odredisni) sektor
                ZauzmiSektor(x, y, z, let.Letelica.RegistracijaAviona, flightId);
                let.TrenutnaX = x;
                let.TrenutnaY = y;
                let.TrenutnaZ = z;

                clientSocket.Send(Encoding.UTF8.GetBytes("DESTINATION_REACHED"));
                return;
            }

            var noviSektor = _vazdusniProstor.GetSektor(x, y, z);
            if (noviSektor == null)
            {
                clientSocket.Send(Encoding.UTF8.GetBytes("POSITION_OK"));
                return;
            }

            // Proveri da li je novi sektor slobodan
            if (noviSektor.Zauzet || noviSektor.AbnormalniVremenskiUslovi)
            {
                // REZOLUCIJA KONFLIKTA
                var alternativa = ResolveConflict(let, x, y, z, flightId);

                if (alternativa.X == let.TrenutnaX && alternativa.Y == let.TrenutnaY && alternativa.Z == let.TrenutnaZ)
                {
                    // Nema alternative - avion ostaje na mestu
                    clientSocket.Send(Encoding.UTF8.GetBytes("POSITION_OK"));
                }
                else
                {
                    // Preusmeravanje
                    var instrukcija = $"REDIRECT:{alternativa.X},{alternativa.Y},{alternativa.Z}";
                    clientSocket.Send(Encoding.UTF8.GetBytes(instrukcija));
                    let.BrojIzmenaputanje++;

                    Console.WriteLine($"[KONFLIKT] Avion {let.Letelica.RegistracijaAviona} preusmeravan na ({alternativa.X},{alternativa.Y},{alternativa.Z})");
                }
                return;
            }

            // Sektor slobodan - odobri pomeranje
            OslobodiSektor(let.TrenutnaX, let.TrenutnaY, let.TrenutnaZ);
            ZauzmiSektor(x, y, z, let.Letelica.RegistracijaAviona, flightId);

            let.TrenutnaX = x;
            let.TrenutnaY = y;
            let.TrenutnaZ = z;

            clientSocket.Send(Encoding.UTF8.GetBytes("POSITION_OK"));
        }

        private static Sektor ResolveConflict(Let let, int targetX, int targetY, int targetZ, int flightId)
        {
            var targetSektor = _vazdusniProstor.GetSektor(targetX, targetY, targetZ);

            // Ako je sektor zauzet od drugog aviona, proveri broj putnika
            if (targetSektor != null && targetSektor.Zauzet && targetSektor.ZauzetOdLetaId > 0)
            {
                if (_aktivniLetovi.TryGetValue(targetSektor.ZauzetOdLetaId, out var drugiLet))
                {
                    // Avion sa manjim brojem putnika se preusmerava
                    if (let.Letelica.TrenutanBrojPutnika < drugiLet.Letelica.TrenutanBrojPutnika)
                    {
                        // Nas avion ima manje putnika - preusmeriti nas avion
                        return FindAlternativeForConflict(let, targetX, targetY, targetZ);
                    }
                    else if (let.Letelica.TrenutanBrojPutnika > drugiLet.Letelica.TrenutanBrojPutnika)
                    {
                        // Drugi avion ima manje putnika - preusmeriti drugog
                        var altZaDrugog = FindAlternativeForConflict(drugiLet, targetX, targetY, targetZ);
                        if (altZaDrugog.X != drugiLet.TrenutnaX || altZaDrugog.Y != drugiLet.TrenutnaY || altZaDrugog.Z != drugiLet.TrenutnaZ)
                        {
                            // Premesti drugog aviona
                            int drugiId = targetSektor.ZauzetOdLetaId;
                            OslobodiSektor(drugiLet.TrenutnaX, drugiLet.TrenutnaY, drugiLet.TrenutnaZ);
                            ZauzmiSektor(altZaDrugog.X, altZaDrugog.Y, altZaDrugog.Z, drugiLet.Letelica.RegistracijaAviona, drugiId);
                            drugiLet.TrenutnaX = altZaDrugog.X;
                            drugiLet.TrenutnaY = altZaDrugog.Y;
                            drugiLet.TrenutnaZ = altZaDrugog.Z;
                            drugiLet.BrojIzmenaputanje++;

                            // Obavesti drugog aviona o preusmeravanju
                            if (_avionSockets.TryGetValue(drugiId, out var drugiSocket))
                            {
                                try
                                {
                                    var msg = $"REDIRECT:{altZaDrugog.X},{altZaDrugog.Y},{altZaDrugog.Z}";
                                    drugiSocket.Send(Encoding.UTF8.GetBytes(msg));
                                }
                                catch { }
                            }

                            // Vrati trazeni sektor nasem avionu
                            return targetSektor;
                        }
                    }
                }
            }

            // Standardna rezolucija - nadji alternativu za nas avion
            return FindAlternativeForConflict(let, targetX, targetY, targetZ);
        }

        private static Sektor FindAlternativeForConflict(Let let, int targetX, int targetY, int targetZ)
        {
            // 1. Pokusaj alternativnu visinu na istom (x,y)
            var altVisina = FindAlternativeAltitude(targetX, targetY, targetZ);
            if (altVisina != null)
                return altVisina;

            // 2. Pokusaj susedne sektore u smeru cilja
            var susedni = _vazdusniProstor.GetSusedniSektori(targetX, targetY, targetZ);

            // Sortiraj po blizini cilju
            susedni.Sort((a, b) =>
            {
                int distA = Math.Abs(let.KrajnjaX - a.X) + Math.Abs(let.KrajnjaY - a.Y);
                int distB = Math.Abs(let.KrajnjaX - b.X) + Math.Abs(let.KrajnjaY - b.Y);
                return distA.CompareTo(distB);
            });

            foreach (var sektor in susedni)
            {
                if (!sektor.Zauzet && !sektor.AbnormalniVremenskiUslovi)
                    return sektor;
            }

            // 3. Nema alternativa - vrati trenutnu poziciju (avion ostaje na mestu)
            return _vazdusniProstor.GetSektor(let.TrenutnaX, let.TrenutnaY, let.TrenutnaZ);
        }

        private static Sektor FindAlternativeAltitude(int x, int y, int currentZ)
        {
            for (int z = 1; z <= _vazdusniProstor.NivoiVisine; z++)
            {
                if (z == currentZ) continue;

                var sektor = _vazdusniProstor.GetSektor(x, y, z);
                if (sektor != null && !sektor.Zauzet && !sektor.AbnormalniVremenskiUslovi)
                    return sektor;
            }
            return null;
        }

        #endregion

        #region Sektor operacije

        private static void OslobodiSektor(int x, int y, int z)
        {
            var sektor = _vazdusniProstor.GetSektor(x, y, z);
            if (sektor != null)
            {
                sektor.Zauzet = false;
                sektor.ZauzetOdAviona = "";
                sektor.ZauzetOdLetaId = 0;
            }
        }

        private static void ZauzmiSektor(int x, int y, int z, string registracija, int flightId)
        {
            var sektor = _vazdusniProstor.GetSektor(x, y, z);
            if (sektor != null)
            {
                sektor.Zauzet = true;
                sektor.ZauzetOdAviona = registracija;
                sektor.ZauzetOdLetaId = flightId;
            }
        }

        #endregion

        #region Upravljanje letovima

        private static void HandleSectorEntered(int flightId, string message)
        {
            if (_aktivniLetovi.TryGetValue(flightId, out var let))
            {
                let.BrojProdjeniSektora++;
            }
        }

        private static void HandleFlightCompleted(int flightId)
        {
            if (_aktivniLetovi.TryGetValue(flightId, out var let))
            {
                OslobodiSektor(let.TrenutnaX, let.TrenutnaY, let.TrenutnaZ);
                _aktivniLetovi.Remove(flightId);
                Console.WriteLine($"\n[LET ZAVRSEN] {let.Letelica.RegistracijaAviona} - " +
                    $"Prodjeni sektori: {let.BrojProdjeniSektora}, Izmene putanje: {let.BrojIzmenaputanje}");
            }

            if (_avionSockets.TryGetValue(flightId, out var socket))
            {
                try { socket.Close(); } catch { }
                _avionSockets.Remove(flightId);
            }
        }

        private static void RemoveFlight(int flightId)
        {
            if (_aktivniLetovi.TryGetValue(flightId, out var let))
            {
                OslobodiSektor(let.TrenutnaX, let.TrenutnaY, let.TrenutnaZ);
                _aktivniLetovi.Remove(flightId);
            }

            if (_avionSockets.TryGetValue(flightId, out var socket))
            {
                try { socket.Close(); } catch { }
                _avionSockets.Remove(flightId);
            }
        }

        private static void UpdateFlightsLoop()
        {
            while (true)
            {
                try
                {
                    Thread.Sleep(2000);

                    lock (_lockObject)
                    {
                        foreach (var kvp in _aktivniLetovi.ToList())
                        {
                            var flightId = kvp.Key;
                            var let = kvp.Value;

                            if (let.TrenutnaX == let.KrajnjaX &&
                                let.TrenutnaY == let.KrajnjaY &&
                                let.TrenutnaZ == let.KrajnjaZ)
                            {
                                if (_avionSockets.TryGetValue(flightId, out var socket))
                                {
                                    try
                                    {
                                        socket.Send(Encoding.UTF8.GetBytes("DESTINATION_REACHED"));
                                    }
                                    catch { }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Greska u update petlji: {ex.Message}");
                }
            }
        }

        #endregion

        #region Prikaz

        private static void DisplayStatus()
        {
            if ((DateTime.Now - _lastDisplayTime).TotalSeconds < 3) return;
            _lastDisplayTime = DateTime.Now;

            lock (_lockObject)
            {
                Console.Clear();
                Console.WriteLine("============================================");
                Console.WriteLine("       SISTEM KONTROLE LETA - SERVER");
                Console.WriteLine("============================================");
                Console.WriteLine($"Vreme: {DateTime.Now:HH:mm:ss}");
                Console.WriteLine();

                // Mapa za svaki nivo visine
                for (int z = 1; z <= _vazdusniProstor.NivoiVisine; z++)
                {
                    Console.WriteLine(_vazdusniProstor.GetMapVisualization(z));
                }

                Console.WriteLine("Legenda: [] = Slobodan | A1,A2.. = Avion | X = Abnormalno vreme");
                Console.WriteLine();

                // Lista aktivnih letova
                Console.WriteLine($"--- Aktivni letovi ({_aktivniLetovi.Count}) ---");
                if (_aktivniLetovi.Count == 0)
                {
                    Console.WriteLine("  Nema aktivnih letova.");
                }
                else
                {
                    foreach (var kvp in _aktivniLetovi)
                    {
                        var let = kvp.Value;
                        Console.WriteLine($"  A{kvp.Key}: {let.Letelica.RegistracijaAviona} " +
                            $"({let.Letelica.NazivAviokompanije}) - Pilot: {let.Letelica.NazivGlavnogPilota}");
                        Console.WriteLine($"       Pozicija: ({let.TrenutnaX},{let.TrenutnaY},{let.TrenutnaZ}) -> " +
                            $"Cilj: ({let.KrajnjaX},{let.KrajnjaY},{let.KrajnjaZ})");
                        Console.WriteLine($"       Putnici: {let.Letelica.TrenutanBrojPutnika}/{let.Letelica.MaksimalanBrojPutnika} | " +
                            $"Prodjeni sektori: {let.BrojProdjeniSektora} | " +
                            $"Preostalo: {let.BrojPreostalihSektora()} | " +
                            $"Izmene putanje: {let.BrojIzmenaputanje}");
                    }
                }

                Console.WriteLine();
                Console.WriteLine($"--- Poslednji zahtevi ({_zahteviZaUlazak.Count} ukupno) ---");
                var recent = _zahteviZaUlazak.Skip(Math.Max(0, _zahteviZaUlazak.Count - 5)).Take(5);
                foreach (var zahtev in recent)
                {
                    Console.WriteLine($"  {zahtev.Let?.Letelica?.RegistracijaAviona} - {zahtev.StatusZahteva} ({zahtev.VremeZahteva:HH:mm:ss})");
                }
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
